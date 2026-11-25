using System.Buffers.Binary;
using SevenZip;

namespace LabelStudio;

public static class Protocol
{
    static void WriteUInt16(byte[] b, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(offset), checked((ushort)value));
    public static byte[] Command(byte code, int param = 0, int count = 0)
    {
        byte[] b = [0x7e, 0x5a, 12, 0, 0x10, 1, 0xaa, code, 0, 0, 0, 1, 0, 0, 0, 0];
        WriteUInt16(b, 12, param);
        WriteUInt16(b, 14, count);
        WriteUInt16(b, 8, b.Skip(10).Sum(v => v));
        return b;
    }

    private const int RasterHeaderSize = 14;
    private const int DataFrameSize = 512;
    private const int DataPayloadSize = 500;
    public static (List<byte[]> Buffers, int Backfeed) RasterBuffers(Raster image, PrinterProfile model, int density = 3, int copies = 1, bool dieCut = false, double gap = 3)
    {
        ValidateRaster(image, model, density, copies, gap);
        var engine = model.Engine;
        var bytesPerColumn = (model.HeadDots + 7) / 8;
        var columns = PackColumns(image, model.HeadDots, bytesPerColumn);
        var feedLimit = engine is "e10" or "e16" ? (dieCut ? (int)((6.5 - gap) * 8) : 48) : 0;
        var trim = LeadingBlankColumns(columns, image.Width, bytesPerColumn, feedLimit);
        var backfeed = trim < feedLimit - 1 ? Math.Max(0, feedLimit - trim) : 0;
        var bufferSize = engine switch
        {
            "t50-old" => 40000,
            "modern" => 4096,
            _ => 4000
        };
        // Firmware reserves eight bytes after the raster, in addition to its header.
        var columnsPerBuffer = engine is "e10" or "e16" or "g7" or "g15" ? 332 : (bufferSize - RasterHeaderSize - 8) / bytesPerColumn;
        var buffers = new List<byte[]>();
        for (var copy = 0; copy < copies; copy++)
        {
            var start = copy == 0 ? trim : 0;
            for (var offset = start; offset < image.Width; offset += columnsPerBuffer)
            {
                var columnCount = Math.Min(columnsPerBuffer, image.Width - offset);
                var first = offset == start;
                var final = offset + columnCount == image.Width;
                var buffer = new byte[bufferSize];
                buffer[2] = (byte)((first ? 2 : 0) | (final ? 4 : 0) | (final && copy == copies - 1 ? 8 : 0));
                buffer[3] = (byte)((density << 2) | (engine is "g15" or "modern" or "t50-old" ? 0x40 : 0x10));
                WriteUInt16(buffer, 4, columnCount);
                buffer[6] = (byte)bytesPerColumn;
                buffer[8] = buffer[10] = (byte)(engine is "g7" or "g15" ? 16 : 1);
                buffer[12] = (byte)(engine == "modern" ? density : copy == 0 && first ? trim : 0);
                Array.Copy(columns, offset * bytesPerColumn, buffer, RasterHeaderSize, columnCount * bytesPerColumn);
                WriteRasterChecksum(buffer, columnCount * bytesPerColumn);
                buffers.Add(buffer);
            }
        }

        return (buffers, backfeed);
    }

    private static void ValidateRaster(Raster image, PrinterProfile model, int density, int copies, double gap)
    {
        if (model.Engine == "cups")
            throw new InvalidOperationException("This model requires an installed system driver.");
        if (image.Height <= 0 || image.Height > model.HeadDots || image.Width < 16 || image.Width > 7200 || image.Pixels.Length != image.Width * image.Height || image.Pixels.Any(value => value is not (0 or 255)))
            throw new InvalidDataException("Raster does not fit this printer.");
        if (density < 1 || density > model.DensityMax || copies < 1 || copies > 20 || !double.IsFinite(gap) || gap < 0 || gap > 6)
            throw new InvalidDataException("Invalid print settings.");
    }

    private static byte[] PackColumns(Raster image, int headDots, int bytesPerColumn)
    {
        var columns = new byte[image.Width * bytesPerColumn];
        var verticalOffset = (headDots - image.Height) / 2;
        for (var x = 0; x < image.Width; x++)
        {
            for (var y = 0; y < image.Height; y++)
            {
                if (image.Pixels[y * image.Width + x] != 0)
                    continue;
                var dot = y + verticalOffset;
                columns[x * bytesPerColumn + dot / 8] |= (byte)(1 << (dot % 8));
            }
        }

        return columns;
    }

    private static int LeadingBlankColumns(byte[] columns, int width, int bytesPerColumn, int limit)
    {
        var firstInk = limit;
        for (var x = 0; x < Math.Min(limit, width); x++)
        {
            if (!columns.AsSpan(x * bytesPerColumn, bytesPerColumn).ContainsAnyExcept((byte)0))
                continue;
            firstInk = x;
            break;
        }

        // Retain one blank column before the first ink, as required by the firmware.
        return Math.Max(0, firstInk - 1);
    }

    private static void WriteRasterChecksum(byte[] buffer, int rasterBytes)
    {
        var checksum = buffer.Skip(2).Take(RasterHeaderSize - 2).Sum(value => value);
        // The APK checksums the header and the last byte of each 256-byte page.
        for (var page = 1; page <= (RasterHeaderSize + rasterBytes) / 256; page++)
            checksum += buffer[page * 256 - 1];
        WriteUInt16(buffer, 0, checksum & 0xffff);
    }

    public static byte[] Compress(byte[] input)
    {
        var encoder = new SevenZip.Compression.LZMA.Encoder();
        encoder.SetCoderProperties([CoderPropID.DictionarySize, CoderPropID.PosStateBits, CoderPropID.LitContextBits, CoderPropID.LitPosBits, CoderPropID.Algorithm, CoderPropID.NumFastBytes, CoderPropID.MatchFinder, CoderPropID.EndMarker], [8192, 2, 3, 0, 2, 128, "bt4", false]);
        using var source = new MemoryStream(input);
        using var output = new MemoryStream();
        encoder.WriteCoderProperties(output);
        output.Write(BitConverter.GetBytes((long)input.Length));
        encoder.Code(source, output, -1, -1, null);
        return output.ToArray();
    }

    public static List<byte[]> CompressBlocks(List<byte[]> buffers, bool modern)
    {
        var result = new List<byte[]>();
        for (var index = 0; index < buffers.Count;)
        {
            var count = modern ? Math.Min(4, buffers.Count - index) : 1;
            byte[] stream;
            while (true)
            {
                stream = Compress(buffers.Skip(index).Take(count).SelectMany(v => v).ToArray());
                if (!modern || stream.Length <= 4096)
                    break;
                if (--count == 0)
                    throw new InvalidDataException("Artwork exceeds the printer's receive buffer.");
            }

            result.Add(stream);
            index += count;
        }

        return result;
    }

    public static List<byte[]> DataFrames(byte[] stream)
    {
        var count = (stream.Length + DataPayloadSize - 1) / DataPayloadSize;
        if (count is < 1 or > 255)
            throw new InvalidDataException("Compressed block is too large.");
        var frames = new List<byte[]>();
        for (int index = 0; index < count; index++)
        {
            var frame = new byte[DataFrameSize];
            byte[] header = [0x7e, 0x5a, 0xfc, 1, 0x10, 2, 0xaa, 0xbb];
            header.CopyTo(frame, 0);
            frame[10] = (byte)index;
            frame[11] = (byte)count;
            Array.Copy(stream, index * DataPayloadSize, frame, 12, Math.Min(DataPayloadSize, stream.Length - index * DataPayloadSize));
            WriteUInt16(frame, 8, frame.Skip(10).Sum(v => v) & 0xffff);
            frames.Add(frame);
        }

        return frames;
    }

    public static void Print(IWire wire, PrinterProfile model, Raster raster, int density, int copies, bool dieCut, double gap, Action<string> progress)
    {
        var (buffers, backfeed) = RasterBuffers(raster, model, density, copies, dieCut, gap);
        var modern = model.Engine == "modern";
        var streams = CompressBlocks(buffers, modern);
        void Wait(Func<PrinterStatus, bool> predicate, int seconds, string error)
        {
            var deadline = Environment.TickCount64 + seconds * 1000L;
            while (Environment.TickCount64 < deadline)
            {
                var s = wire.Status();
                s.Check();
                if (predicate(s))
                    return;
                Thread.Sleep(100);
            }

            throw new TimeoutException(error);
        }

        progress("Checking printer…");
        var state = wire.Status();
        state.Check();
        if (state.Busy || state.Printing)
            throw new IOException("The printer is busy. Wait for it to finish.");
        if (model.Engine != "e10")
            wire.Send(0x12);
        if (model.Engine is "e10" or "e16")
            wire.Send(0xc9, 80 + (density - 1) * 10);
        else if (model.Engine == "g7")
            wire.Send(0xc9, 75 + (density - 1) * 15);
        var started = false;
        try
        {
            // Even a lost acknowledgement can mean the printer has entered print mode.
            started = true;
            wire.Send(0x13);
            if (model.Engine != "e16")
                Wait(s => s.Printing, 6, "Printer did not enter print mode.");
            if (model.Engine is "e10" or "e16")
                wire.Send(0xba, backfeed);
            for (int i = 0; i < streams.Count; i++)
            {
                progress($"Sending label {i + 1}/{streams.Count}…");
                Wait(s => !s.Full, 30, "Printer buffer stayed full.");
                wire.Upload(streams[i], streams.Count > 1 ? 20 : 60, modern);
            }

            progress("Waiting for printer to finish…");
            Thread.Sleep(150);
            Wait(s => !s.Printing && !s.Busy && !s.Full, 60, "Completion was not confirmed. Check the printer before printing again.");
            started = false;
        }
        finally
        {
            if (started)
                try
                {
                    wire.Send(0x14);
                }
                catch
                { /* Never replay a partially transmitted job. */
                }
        }
    }
}
