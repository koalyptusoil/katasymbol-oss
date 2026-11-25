using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace LabelStudio;

// BlueZ via the system GIO D-Bus library, without a helper process.
[SupportedOSPlatform("linux")]
public sealed class BleWire : FramedWire
{
    readonly BlockingCollection<byte[]> incoming = new();
    readonly Gio.SignalCallback callback;
    IntPtr bus, context;
    Thread? pump;
    volatile bool stopping;
    uint subscription;
    string device = "", writePath = "", notifyPath = "";
    bool response;
    public BleWire(string mac)
    {
        if (!Printers.IsBluetoothAddress(mac))
            throw new InvalidDataException("Enter a Bluetooth printer address.");
        callback = OnSignal;
        try
        {
            bus = Gio.g_bus_get_sync(1, IntPtr.Zero, out var error);
            Gio.Check(error);
            var tree = Call("/", "org.freedesktop.DBus.ObjectManager", "GetManagedObjects");
            device = Regex.Matches(tree, @"'(/org/bluez/[^']+/dev_[^/']+)'\s*:").Select(m => m.Groups[1].Value).FirstOrDefault(p => p.EndsWith("dev_" + mac.Replace(':', '_'), StringComparison.OrdinalIgnoreCase)) ?? throw new IOException("Bluetooth printer is not known. Use Find printer first.");
            Call(device, "org.bluez.Device1", "Connect");
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!Call(device, "org.freedesktop.DBus.Properties", "Get", "('org.bluez.Device1', 'ServicesResolved')").Contains("true"))
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Bluetooth services did not become ready.");
                Thread.Sleep(100);
            }

            tree = Call("/", "org.freedesktop.DBus.ObjectManager", "GetManagedObjects");
            var paths = Regex.Matches(tree, @"'(/org/bluez/[^']+)'\s*:\s*\{").ToArray();
            var chars = new Dictionary<string, (string Path, string Data)>();
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i].Groups[1].Value;
                if (!path.StartsWith(device + "/"))
                    continue;
                var end = i + 1 < paths.Length ? paths[i + 1].Index : tree.Length;
                var data = tree[paths[i].Index..end];
                if (!data.Contains("org.bluez.GattCharacteristic1"))
                    continue;
                var uuid = Regex.Match(data, @"'UUID':\s*<'([^']+)'>").Groups[1].Value.ToLowerInvariant();
                if (uuid.Length > 0)
                    chars[uuid] = (path, data);
            }

            (string Notify, string Write)[] pairs = [("fec1", "fec1"), ("ffe1", "ffe9"), ("c305", "c302"), ("ff01", "ff02")];
            foreach (var pair in pairs)
            {
                string Uuid(string s) => "0000" + s + "-0000-1000-8000-00805f9b34fb";
                if (!chars.TryGetValue(Uuid(pair.Notify), out var n) || !chars.TryGetValue(Uuid(pair.Write), out var w))
                    continue;
                notifyPath = n.Path;
                writePath = w.Path;
                response = w.Data.Contains("'write'");
                break;
            }

            if (writePath.Length == 0)
                throw new IOException("Printer has no recognized Katasymbol BLE service.");
            using var ready = new ManualResetEventSlim();
            pump = new Thread(() =>
            {
                context = Gio.g_main_context_new();
                Gio.g_main_context_push_thread_default(context);
                subscription = Gio.g_dbus_connection_signal_subscribe(bus, "org.bluez", "org.freedesktop.DBus.Properties", "PropertiesChanged", notifyPath, "org.bluez.GattCharacteristic1", 0, callback, IntPtr.Zero, IntPtr.Zero);
                ready.Set();
                while (!stopping)
                {
                    while (Gio.g_main_context_iteration(context, false))
                    {
                    }

                    Thread.Sleep(5);
                }

                Gio.g_dbus_connection_signal_unsubscribe(bus, subscription);
                Gio.g_main_context_pop_thread_default(context);
                Gio.g_main_context_unref(context);
            })
            {
                IsBackground = true,
                Name = "Printer notifications"
            };
            pump.Start();
            ready.Wait();
            Call(notifyPath, "org.bluez.GattCharacteristic1", "StartNotify");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    void OnSignal(IntPtr connection, IntPtr sender, IntPtr path, IntPtr iface, IntPtr signal, IntPtr parameters, IntPtr user)
    {
        try
        {
            var text = Gio.Print(parameters);
            var value = Regex.Match(text, @"'Value':\s*<\[([^\]]*)\]>");
            if (value.Success)
            {
                var bytes = Regex.Matches(value.Groups[1].Value, @"0x([0-9a-fA-F]{2})").Select(m => Convert.ToByte(m.Groups[1].Value, 16)).ToArray();
                if (bytes.Length > 0 && !stopping)
                    incoming.Add(bytes);
            }
        }
        catch
        { /* Exceptions cannot cross a native callback boundary. */
        }
    }

    string Call(string path, string iface, string method, string? arguments = null)
    {
        IntPtr args = IntPtr.Zero;
        if (arguments != null)
        {
            args = Gio.g_variant_parse(IntPtr.Zero, arguments, IntPtr.Zero, IntPtr.Zero, out var parseError);
            Gio.Check(parseError);
            Gio.g_variant_ref_sink(args);
        }

        try
        {
            var result = Gio.g_dbus_connection_call_sync(bus, "org.bluez", path, iface, method, args, IntPtr.Zero, 0, 20000, IntPtr.Zero, out var error);
            Gio.Check(error);
            try
            {
                return Gio.Print(result);
            }
            finally
            {
                Gio.g_variant_unref(result);
            }
        }
        finally
        {
            if (args != IntPtr.Zero)
                Gio.g_variant_unref(args);
        }
    }

    protected override void Write(byte[] data)
    {
        // 20-byte chunks fit the minimum BLE MTU on every supported firmware.
        for (int i = 0; i < data.Length; i += 20)
        {
            var bytes = string.Join(", ", data.Skip(i).Take(20).Select(b => "0x" + b.ToString("x2")));
            Call(writePath, "org.bluez.GattCharacteristic1", "WriteValue", "(@ay [" + bytes + "], {'type': <'" + (response ? "request" : "command") + "'>})");
            Thread.Sleep(10);
        }
    }

    protected override byte[] Read(int timeoutMs) => incoming.TryTake(out var data, timeoutMs) ? data : throw new TimeoutException("Bluetooth LE printer did not reply.");
    public override void Dispose()
    {
        if (bus == IntPtr.Zero)
            return;
        if (notifyPath.Length > 0)
            try
            {
                Call(notifyPath, "org.bluez.GattCharacteristic1", "StopNotify");
            }
            catch
            {
            }

        stopping = true;
        pump?.Join(2000);
        if (device.Length > 0)
            try
            {
                Call(device, "org.bluez.Device1", "Disconnect");
            }
            catch
            {
            }

        Gio.g_object_unref(bus);
        bus = IntPtr.Zero;
    }
}

[SupportedOSPlatform("linux")]
internal static class Gio
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SignalCallback(IntPtr connection, IntPtr sender, IntPtr path, IntPtr iface, IntPtr signal, IntPtr parameters, IntPtr user);
    [StructLayout(LayoutKind.Sequential)]
    struct GError
    {
        public uint Domain;
        public int Code;
        public IntPtr Message;
    }

    internal static void Check(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return;
        var e = Marshal.PtrToStructure<GError>(error);
        var message = Marshal.PtrToStringUTF8(e.Message);
        g_error_free(error);
        throw new IOException(message ?? "Bluetooth service error.");
    }

    internal static string Print(IntPtr variant)
    {
        var text = g_variant_print(variant, true);
        try
        {
            return Marshal.PtrToStringUTF8(text) ?? "";
        }
        finally
        {
            g_free(text);
        }
    }

    [DllImport("libgobject-2.0.so.0")]
    internal static extern void g_object_unref(IntPtr obj);
    [DllImport("libgio-2.0.so.0")]
    internal static extern IntPtr g_bus_get_sync(int type, IntPtr cancellable, out IntPtr error);
    [DllImport("libgio-2.0.so.0")]
    internal static extern IntPtr g_dbus_connection_call_sync(IntPtr connection, string bus, string path, string iface, string method, IntPtr args, IntPtr replyType, int flags, int timeout, IntPtr cancellable, out IntPtr error);
    [DllImport("libgio-2.0.so.0")]
    internal static extern uint g_dbus_connection_signal_subscribe(IntPtr connection, string sender, string iface, string member, string path, string arg0, int flags, SignalCallback callback, IntPtr user, IntPtr destroy);
    [DllImport("libgio-2.0.so.0")]
    internal static extern void g_dbus_connection_signal_unsubscribe(IntPtr connection, uint id);
    [DllImport("libglib-2.0.so.0")]
    internal static extern IntPtr g_variant_parse(IntPtr type, string text, IntPtr limit, IntPtr end, out IntPtr error);
    [DllImport("libglib-2.0.so.0")]
    internal static extern IntPtr g_variant_ref_sink(IntPtr variant);
    [DllImport("libglib-2.0.so.0")]
    internal static extern IntPtr g_variant_print(IntPtr variant, [MarshalAs(UnmanagedType.Bool)] bool annotate);
    [DllImport("libglib-2.0.so.0")]
    internal static extern void g_variant_unref(IntPtr variant);
    [DllImport("libglib-2.0.so.0")]
    internal static extern void g_free(IntPtr ptr);
    [DllImport("libglib-2.0.so.0")]
    internal static extern void g_error_free(IntPtr ptr);
    [DllImport("libglib-2.0.so.0")]
    internal static extern IntPtr g_main_context_new();
    [DllImport("libglib-2.0.so.0")]
    internal static extern void g_main_context_push_thread_default(IntPtr context);
    [DllImport("libglib-2.0.so.0")]
    internal static extern void g_main_context_pop_thread_default(IntPtr context);
    [DllImport("libglib-2.0.so.0")]
    internal static extern void g_main_context_unref(IntPtr context);
    [DllImport("libglib-2.0.so.0")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool g_main_context_iteration(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool block);
}
