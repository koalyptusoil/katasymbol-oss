using System.Text.RegularExpressions;

namespace LabelStudio;

public record Device(string Address, string Name, PrinterProfile? Model = null)
{
    public override string ToString() => Name + "  ·  " + Address;
}

// The editor and protocol layer depend on this facade, never on native libraries.
public static class Printers
{
    public static bool IsBluetoothAddress(string address) => Regex.IsMatch(address, @"\A(?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2}\z");
    public static List<Device> Discover(Action<string> progress)
    {
        if (OperatingSystem.IsLinux())
            return LinuxDiscovery.Discover(progress);
        progress("Looking for installed printers…");
        return SystemPrinting.Queues().Select(queue => new Device(SystemPrinting.TargetPrefix + queue, queue)).ToList();
    }

    public static string Pair(string address)
    {
        if (!OperatingSystem.IsLinux())
            throw DirectPrintingUnavailable();
        return LinuxDiscovery.Pair(address);
    }

    public static IWire Connect(PrinterProfile model, string transport, string target, int channel)
    {
        if (!OperatingSystem.IsLinux())
            throw DirectPrintingUnavailable();
        if (model.Engine == "cups")
            throw new InvalidOperationException("This model needs a system printer queue. Choose System printer and its queue.");
        if (transport == "auto")
            transport = target.StartsWith("/dev/", StringComparison.Ordinal) ? "usb" : model.Transport;
        return transport switch
        {
            "classic" => new ClassicWire(target, channel),
            "usb" when model.Engine == "modern" => new UsbWire(target),
            "usb" => throw new InvalidOperationException("Direct USB needs a modern T50/T80/TL8 profile."),
            "ble" => new BleWire(target),
            _ => throw new InvalidOperationException("Choose a printer connection.")
        };
    }

    private static PlatformNotSupportedException DirectPrintingUnavailable() => new("Direct Bluetooth and USB connections currently require Linux. Choose an installed system printer on Windows or macOS.");
}
