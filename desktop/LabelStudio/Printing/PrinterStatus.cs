namespace LabelStudio;

public record PrinterStatus(bool Full, bool Busy, bool Printing, int Count, string[] Errors)
{
    public static PrinterStatus Parse(byte[] frame)
    {
        if (frame.Length < 20 || !frame.AsSpan(4, 4).SequenceEqual(new byte[] { 0x10, 3, 0x55, 0x11 }))
            throw new IOException("Invalid printer status reply.");
        int a = frame[14], b = frame[15], c = frame[16], d = frame[17];
        (bool, string)[] flags = [((a & 2) != 0, "Cannot read label roll"), ((a & 4) != 0, "Label roll is empty"), ((a & 8) != 0, "Wrong label type"), ((a & 16) != 0, "Label roll is not installed"), ((a & 64) != 0, "Battery is low"), ((b & 8) != 0, "Printhead is too hot"), ((c & 8) != 0, "Printer cover is open"), ((d & 1) != 0, "Install the label roll")];
        return new((a & 1) != 0, (b & 4) != 0, (c & 64) != 0, frame[18] + (frame[19] << 8), flags.Where(f => f.Item1).Select(f => f.Item2).ToArray());
    }

    public void Check()
    {
        if (Errors.Length > 0)
            throw new IOException(string.Join("; ", Errors));
    }

    public override string ToString() => Errors.Length > 0 ? string.Join("; ", Errors) : Busy || Printing ? "Printer is busy." : "Printer ready.";
}
