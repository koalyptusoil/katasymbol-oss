namespace LabelStudio;

public abstract class FramedWire : IWire
{
    readonly FrameReader reader = new();
    protected abstract void Write(byte[] bytes);
    protected abstract byte[] Read(int timeoutMs);
    protected virtual bool AckPackets => false;

    protected byte[] Reply(byte code, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var frame = reader.Pop();
            if (frame != null)
            {
                if (frame[4] == 0x10 && frame[5] == 3 && frame[6] == 0x55 && frame[7] == code)
                    return frame;
                continue;
            }

            reader.Feed(Read((int)Math.Max(1, deadline - Environment.TickCount64)));
        }

        throw new TimeoutException($"Printer did not acknowledge command {code:X2}.");
    }

    public byte[] Send(byte code, int param = 0, int count = 0)
    {
        Write(Protocol.Command(code, param, count));
        return Reply(code);
    }

    public PrinterStatus Status() => PrinterStatus.Parse(Send(0x11));
    public void Upload(byte[] stream, int speed, bool modern)
    {
        var frames = Protocol.DataFrames(stream);
        Send(0x5c, 512, frames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            Write(frames[i]);
            if (AckPackets && (!modern || i < frames.Count - 1))
                Reply(0xbb);
            if (AckPackets)
                Thread.Sleep(30);
        }

        Thread.Sleep(50);
        Send(0x10, modern ? stream.Length : 0, modern ? speed : 0);
        Thread.Sleep(50);
    }

    public abstract void Dispose();
}
