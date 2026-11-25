using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LabelStudio;

[SupportedOSPlatform("linux")]
public sealed class UsbWire : IWire
{
    int fd = -1;
    public UsbWire(string path)
    {
        if (!LinuxDiscovery.UsbDevices().Any(d => d.Address == path))
            throw new InvalidDataException("Choose a detected Supvan USB HID printer.");
        fd = Unix.open(path, 2 | 0x800 | 0x80000);
        if (fd < 0)
            throw Unix.Error("Cannot open USB printer. Access may need the supplied udev rule");
    }

    void Write(byte[] data)
    {
        var report = new byte[64];
        data.CopyTo(report, 0);
        Unix.Wait(fd, 4, 5000);
        if (Unix.write(fd, report, 64) != 64)
            throw Unix.Error("USB write failed");
    }

    public byte[] Send(byte code, int param = 0, int count = 0)
    {
        byte[] b = count > 0 ? new byte[10] : new byte[8];
        b[0] = 0xc0;
        b[1] = 0x40;
        b[2] = (byte)(param >> 8);
        b[3] = (byte)param;
        b[4] = code;
        b[6] = 8;
        if (count > 0)
        {
            b[8] = (byte)(count >> 8);
            b[9] = (byte)count;
        }

        Write(b);
        Unix.Wait(fd, 1, 5000);
        var result = new byte[64];
        var n = Unix.read(fd, result, 64);
        if (n <= 0)
            throw new IOException("USB printer disconnected.");
        return result.Take((int)n).ToArray();
    }

    public PrinterStatus Status()
    {
        var reply = Send(0x11);
        if (reply.Length < 7)
            throw new IOException("USB status reply is too short.");
        var frame = new byte[20];
        new byte[]
        {
            0x10,
            3,
            0x55,
            0x11
        }.CopyTo(frame, 4);
        Array.Copy(reply, 1, frame, 14, 6);
        return PrinterStatus.Parse(frame);
    }

    public void Upload(byte[] stream, int speed, bool modern)
    {
        Send(0x5c, stream.Length);
        for (var i = 0; i < stream.Length; i += 64)
        {
            Write(stream.Skip(i).Take(64).ToArray());
            Thread.Sleep(1);
        }

        Thread.Sleep(50);
        Send(0x10, stream.Length, speed);
        Thread.Sleep(50);
    }

    public void Dispose()
    {
        if (fd >= 0)
        {
            Unix.close(fd);
            fd = -1;
        }
    }
}
