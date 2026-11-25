namespace LabelStudio;

public sealed class FrameReader
{
    readonly List<byte> buffer = [];
    public void Feed(byte[] data) => buffer.AddRange(data);
    public byte[]? Pop()
    {
        while (buffer.Count >= 4)
        {
            if (buffer[0] != 0x7e || buffer[1] != 0x5a)
            {
                buffer.RemoveAt(0);
                continue;
            }

            int length = buffer[2] + (buffer[3] << 8) + 4;
            if (length is < 8 or > 4096)
            {
                buffer.RemoveAt(0);
                continue;
            }

            if (buffer.Count < length)
                return null;
            var frame = buffer.Take(length).ToArray();
            buffer.RemoveRange(0, length);
            return frame;
        }

        return null;
    }
}
