using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LabelStudio;

[SupportedOSPlatform("linux")]
public sealed class ClassicWire : FramedWire
{
    int fd = -1;
    protected override bool AckPackets => true;

    public ClassicWire(string mac, int channel)
    {
        if (!Printers.IsBluetoothAddress(mac) || channel is < 1 or > 30)
            throw new InvalidDataException("Choose a Bluetooth printer and channel 1–30.");
        fd = Unix.socket(31, 1 | 0x80000, 3);
        if (fd < 0)
            throw Unix.Error("Could not open Bluetooth");
        try
        {
            var address = new byte[10];
            address[0] = 31;
            var bytes = mac.Split(':').Select(s => Convert.ToByte(s, 16)).Reverse().ToArray();
            bytes.CopyTo(address, 2);
            address[8] = (byte)channel;
            // Use nonblocking connect with a bounded poll rather than freezing on an offline device.
            Unix.fcntl(fd, 4, 0x800);
            var result = Unix.connect(fd, address, address.Length);
            if (result < 0 && Marshal.GetLastPInvokeError() != 115)
                throw Unix.Error("Bluetooth connection failed");
            if (result < 0)
            {
                Unix.Wait(fd, 4, 10000);
                int error = 0, len = 4;
                Unix.getsockopt(fd, 1, 4, ref error, ref len);
                if (error != 0)
                    throw new IOException("Bluetooth connection failed: " + new System.ComponentModel.Win32Exception(error).Message);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    protected override void Write(byte[] data)
    {
        for (var offset = 0; offset < data.Length; offset += 128)
        {
            if (offset > 0)
                Thread.Sleep(50);
            var piece = data.Skip(offset).Take(128).ToArray();
            int sent = 0;
            while (sent < piece.Length)
            {
                Unix.Wait(fd, 4, 8000);
                var part = piece.Skip(sent).ToArray();
                var n = Unix.write(fd, part, (nuint)part.Length);
                if (n <= 0)
                    throw Unix.Error("Bluetooth write failed");
                sent += (int)n;
            }
        }
    }

    protected override byte[] Read(int timeoutMs)
    {
        Unix.Wait(fd, 1, timeoutMs);
        var b = new byte[4096];
        var n = Unix.read(fd, b, (nuint)b.Length);
        if (n <= 0)
            throw new IOException("Printer closed the Bluetooth connection.");
        return b.Take((int)n).ToArray();
    }

    public override void Dispose()
    {
        if (fd >= 0)
        {
            Unix.close(fd);
            fd = -1;
        }
    }
}
