using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LabelStudio;

[SupportedOSPlatform("linux")]
internal static class Unix
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        public int Fd;
        public short Events, Returned;
    }

    [DllImport("libc", SetLastError = true)]
    internal static extern int socket(int family, int type, int protocol);
    [DllImport("libc", SetLastError = true)]
    internal static extern int connect(int fd, byte[] address, int length);
    [DllImport("libc", SetLastError = true)]
    internal static extern int getsockopt(int fd, int level, int option, ref int value, ref int len);
    [DllImport("libc", SetLastError = true)]
    internal static extern int fcntl(int fd, int cmd, int value);
    [DllImport("libc", SetLastError = true)]
    internal static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)]
    internal static extern int close(int fd);
    [DllImport("libc", SetLastError = true)]
    internal static extern nint read(int fd, byte[] data, nuint count);
    [DllImport("libc", SetLastError = true)]
    internal static extern nint write(int fd, byte[] data, nuint count);
    [DllImport("libc", SetLastError = true)]
    static extern int poll(ref PollFd fd, nuint count, int timeout);
    internal static IOException Error(string message) => new(message + ": " + new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()).Message);
    internal static void Wait(int fd, short events, int timeout)
    {
        var p = new PollFd
        {
            Fd = fd,
            Events = events
        };
        var n = poll(ref p, 1, timeout);
        if (n == 0)
            throw new TimeoutException("Printer did not respond.");
        if (n < 0 || (p.Returned & (8 | 16 | 32)) != 0)
            throw new IOException("Printer connection was lost.");
    }
}
