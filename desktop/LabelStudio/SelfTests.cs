using System.Buffers.Binary;
using System.Text.Json;
using SkiaSharp;

namespace LabelStudio;

// Exercises the shipped binary without a printer or desktop session.
public static class SelfTests
{
    public static int Run()
    {
        int passed = 0;
        void Test(string name, Action action)
        {
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }

        static void Check(bool b, string message = "Assertion failed")
        {
            if (!b)
                throw new Exception(message);
        }

        static byte[] Decode(byte[] encoded)
        {
            var decoder = new SevenZip.Compression.LZMA.Decoder();
            decoder.SetDecoderProperties(encoded[..5]);
            using var src = new MemoryStream(encoded[13..]);
            using var dst = new MemoryStream();
            decoder.Code(src, dst, encoded.Length - 13, BitConverter.ToInt64(encoded, 5), null!);
            return dst.ToArray();
        }

        try
        {
            Test("Android command frames", () =>
            {
                Check(Convert.ToHexString(Protocol.Command(0x11)).ToLowerInvariant() == "7e5a0c001001aa110100000100000000");
                Check(Convert.ToHexString(Protocol.Command(0x5c, 512, 1)).ToLowerInvariant() == "7e5a0c001001aa5c0400000100020100");
            });
            Test("E10 golden header and LSB raster", () =>
            {
                var pixels = Enumerable.Repeat((byte)255, 240 * 96).ToArray();
                pixels[8] = pixels[9 * 240 + 8] = 0;
                var (buffers, backfeed) = Protocol.RasterBuffers(new(240, 96, pixels), Catalog.Get("e10"));
                var b = buffers[0];
                Check(Convert.ToHexString(b[..14]).ToLowerInvariant() == "28010e1ce9000c00010001000700");
                Check(b[26] == 1 && b[27] == 2 && backfeed == 41 && b.Length == 4000);
            });
            Test("All direct profiles preserve every ink dot and checksum", () =>
            {
                foreach (var model in Catalog.Models.Where(m => m.Engine != "cups"))
                {
                    var w = 700;
                    var h = model.HeadDots;
                    var pixels = Enumerable.Repeat((byte)255, w * h).ToArray();
                    for (int x = 8; x < 680; x++)
                    {
                        pixels[(x % h) * w + x] = 0;
                        pixels[(h - 2) * w + x] = 0;
                    }

                    var (buffers, _) = Protocol.RasterBuffers(new(w, h, pixels), model);
                    var reconstructed = Enumerable.Repeat((byte)255, w * h).ToArray();
                    var column = model.Engine is "e10" or "e16" ? buffers[0][12] : 0;
                    foreach (var b in buffers)
                    {
                        int width = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4)), bpc = b[6], sum = b.Skip(2).Take(12).Sum(v => v);
                        for (int k = 1; k <= (14 + width * bpc) / 256; k++)
                            sum += b[k * 256 - 1];
                        Check(BinaryPrimitives.ReadUInt16LittleEndian(b) == (sum & 0xffff), model.Id + " checksum");
                        for (int x = 0; x < width; x++)
                            for (int y = 0; y < h; y++)
                                if ((b[14 + x * bpc + y / 8] & (1 << (y % 8))) != 0)
                                    reconstructed[y * w + column + x] = 0;
                        column += width;
                    }

                    Check(pixels.SequenceEqual(reconstructed), model.Id + " raster");
                    Check((buffers[0][2] & 2) != 0 && (buffers[^1][2] & 12) == 12);
                    var streams = Protocol.CompressBlocks(buffers, model.Engine == "modern");
                    Check(streams.SelectMany(Decode).SequenceEqual(buffers.SelectMany(v => v)), model.Id + " compression");
                    if (model.Engine == "modern")
                        Check(streams.All(s => s.Length <= 4096));
                }
            });
            Test("LZMA firmware settings and no end marker", () =>
            {
                var input = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
                var encoded = Protocol.Compress(input);
                Check(encoded.Take(5).SequenceEqual(new byte[] { 0x5d, 0, 0x20, 0, 0 }));
                Check(Decode(encoded).SequenceEqual(input));
            });
            Test("Compression matches the original Java APK encoder byte for byte", () =>
            {
                var input = System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Linux labels", 300)));
                Check(Convert.ToHexString(Protocol.Compress(input)).ToLowerInvariant() == "5d00200000100e00000000000000261a49c78955b30471ef33f2790fcfd11eab22a58d3da40fd244eb71a395a7cea49f316fc000");
            });
            if (OperatingSystem.IsLinux())
                Test("Native BlueZ variant and event-context interop", () =>
                {
                    var variant = Gio.g_variant_parse(IntPtr.Zero, "(@ay [0x7e, 0x5a], {'type': <'command'>})", IntPtr.Zero, IntPtr.Zero, out var error);
                    Gio.Check(error);
                    Gio.g_variant_ref_sink(variant);
                    try
                    {
                        Check(Gio.Print(variant).Contains("0x7e"));
                    }
                    finally
                    {
                        Gio.g_variant_unref(variant);
                    }

                    var context = Gio.g_main_context_new();
                    try
                    {
                        Check(!Gio.g_main_context_iteration(context, false));
                    }
                    finally
                    {
                        Gio.g_main_context_unref(context);
                    }
                });
            Test("Packet indexes, checksums, zero padding", () =>
            {
                var input = Enumerable.Range(0, 1280).Select(i => (byte)i).ToArray();
                var frames = Protocol.DataFrames(input);
                Check(frames.Count == 3);
                for (int i = 0; i < frames.Count; i++)
                {
                    var f = frames[i];
                    Check(f.Length == 512 && f[10] == i && f[11] == 3);
                    Check(BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(8)) == (f.Skip(10).Sum(v => v) & 0xffff));
                }

                var data = frames.SelectMany(f => f.Skip(12)).ToArray();
                Check(data.Take(input.Length).SequenceEqual(input));
                Check(data.Skip(input.Length).All(v => v == 0));
            });
            Test("Fragmented frames, noise and resynchronization", () =>
            {
                var reader = new FrameReader();
                byte[] ack = [0x7e, 0x5a, 4, 0, 0x10, 3, 0x55, 0xbb];
                reader.Feed([1, 2, 0x7e]);
                Check(reader.Pop() == null);
                reader.Feed(ack.Skip(1).Take(2).ToArray());
                Check(reader.Pop() == null);
                reader.Feed(ack.Skip(3).Concat(ack).ToArray());
                Check(reader.Pop()!.SequenceEqual(ack));
                Check(reader.Pop()!.SequenceEqual(ack));
                reader.Feed(new byte[] { 0x7e, 0x5a, 0xff, 0xff }.Concat(ack).ToArray());
                Check(reader.Pop()!.SequenceEqual(ack));
            });
            Test("Copies mark only final job end", () =>
            {
                var raster = Rasterizer.Render(new());
                var (buffers, _) = Protocol.RasterBuffers(raster, Catalog.Get("e10"), copies: 3);
                Check(buffers.Count(b => (b[2] & 2) != 0) == 3 && buffers.Count(b => (b[2] & 4) != 0) == 3 && buffers.Count(b => (b[2] & 8) != 0) == 1);
            });
            Test("Multilingual text, exact dimensions and monochrome output", () =>
            {
                foreach (var text in new[]
                {
                    "Made on Linux",
                    "مرحبا بالعالم",
                    "नमस्ते",
                    "Tea ☕"
                }

                )
                {
                    var doc = new LabelDocument();
                    doc.Objects[0].Text = text;
                    var raster = Rasterizer.Render(doc);
                    Check(raster.Width == 320 && raster.Height == 96);
                    Check(raster.Pixels.Any(v => v == 0) && raster.Pixels.Any(v => v == 255));
                    Check(raster.Pixels.All(v => v is 0 or 255));
                    using var png = SKBitmap.Decode(raster.Png());
                    Check(png.Width == 320 && png.Height == 96);
                    for (int y = 0; y < 96; y++)
                        for (int x = 0; x < 320; x++)
                            Check(png.GetPixel(x, y).Red == raster.Pixels[y * 320 + x]);
                }

                var hi = new LabelDocument
                {
                    ModelId = "t50_plus_",
                    HeightMm = 24
                };
                Check(Rasterizer.Render(hi).Width == 480 && Rasterizer.Render(hi).Height == 288);
            });
            Test("Automatic length follows shared text metrics without resizing the font", () =>
            {
                var doc = new LabelDocument
                {
                    AutoLength = true
                };
                var o = doc.Objects[0];
                o.Text = "iii";
                o.Align = "left";
                var narrow = Rasterizer.Render(doc);
                var first = doc.LengthMm;
                o.Text = "WWW";
                var wide = Rasterizer.Render(doc);
                Check(doc.LengthMm > first && wide.Width > narrow.Width && o.FontMm == 4);
                Check(doc.LengthMm - o.X - o.Width >= 2 && doc.LengthMm - o.X - o.Width < 2 + 1 / doc.Profile.DotsPerMm);
                using (var geometry = new TextGeometry(o, doc.Profile.DotsPerMm))
                    Check(geometry.Caret(0).Y == geometry.Caret(3).Y, "Auto text must not wrap");
                var reference = doc.Clone();
                reference.AutoLength = false;
                reference.LengthMm = 150;
                reference.Objects[0].Width = 146;
                Check(wide.Pixels.Count(p => p == 0) == Rasterizer.Render(reference).Pixels.Count(p => p == 0), "Auto fit preserves every ink dot");
                o.FontMm = 6;
                doc.ApplyAutoLength();
                Check(doc.LengthMm > wide.Width / doc.Profile.DotsPerMm);
                o.Text = "";
                doc.ApplyAutoLength();
                Check(doc.LengthMm == 10);
                doc.AutoLength = false;
                o.Text = new string('W', 100);
                doc.ApplyAutoLength();
                Check(doc.LengthMm == 10, "Fixed size remains fixed");
            });
            Test("Automatic multiline, Unicode, frames, fixed layers and length limit", () =>
            {
                var doc = new LabelDocument
                {
                    AutoLength = true
                };
                var o = doc.Objects[0];
                o.FontMm = 2;
                o.Text = "Tea ☕ مرحبا";
                doc.ApplyAutoLength();
                var longest = doc.LengthMm;
                o.Text += "\nHi";
                doc.ApplyAutoLength();
                Check(doc.LengthMm == longest, "Longest explicit line sets length");
                doc.Objects.Insert(0, new()
                {
                    Kind = "border",
                    X = .5,
                    Y = .5,
                    Width = doc.LengthMm - 1,
                    Height = 11
                });
                o.Text = "Tea";
                doc.ApplyAutoLength();
                Check(doc.LengthMm < longest && doc.Objects[0].Width == doc.LengthMm - 1, "Frame follows shrinking tape");
                o.Text = "A much longer title";
                doc.ApplyAutoLength();
                Check(doc.Objects[0].Width == doc.LengthMm - 1, "Frame follows growing tape");
                var fixedLayer = new LabelObject
                {
                    Kind = "image",
                    X = 2,
                    Y = 1,
                    Width = 50,
                    Height = 10,
                    Locked = true
                };
                doc.LengthMm = Math.Max(52, doc.LengthMm);
                doc.Objects.Add(fixedLayer);
                o.Text = "Hi";
                doc.ApplyAutoLength();
                Check(doc.LengthMm >= 52 && fixedLayer.Width == 50 && fixedLayer.X == 2, "Fixed artwork preserved");
                o.Text = new string('W', 2000);
                Check(doc.ApplyAutoLength() && doc.LengthMm == 300);
                doc.Validate();
                Check(Rasterizer.Render(doc).Width == 2400);
                o.Text = "Tea";
                doc.ApplyAutoLength();
                Check(doc.LengthMm < 300);
                var serialized = doc.Serialize();
                Check(doc.Serialize() == serialized, "Layout is idempotent");
                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".labelstudio");
                try
                {
                    doc.Save(temp);
                    var loaded = LabelDocument.Load(temp);
                    Check(loaded.AutoLength && loaded.Serialize() == serialized);
                }
                finally
                {
                    File.Delete(temp);
                }

                Check(!new LabelDocument().AutoLength, "Existing documents keep fixed length");
            });
            Test("Smoother display never changes printer dots", () =>
            {
                var raster = Rasterizer.Render(new());
                var before = raster.Pixels.ToArray();
                using var preview = SKBitmap.Decode(raster.PreviewPng());
                Check(preview.Pixels.Any(p => p.Red > 0 && p.Red < 255));
                Check(raster.Pixels.SequenceEqual(before));
                var doc = new LabelDocument();
                var first = Protocol.RasterBuffers(raster, doc.Profile).Buffers;
                var second = Protocol.RasterBuffers(Rasterizer.Render(doc), doc.Profile).Buffers;
                Check(first.SelectMany(v => v).SequenceEqual(second.SelectMany(v => v)));
            });
            Test("Portable layered design roundtrip", () =>
            {
                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".labelstudio");
                try
                {
                    var doc = new LabelDocument();
                    doc.Objects.Add(new()
                    {
                        Kind = "border",
                        X = .5,
                        Y = .5,
                        Width = 39,
                        Height = 11
                    });
                    doc.Save(temp);
                    var loaded = LabelDocument.Load(temp);
                    Check(doc.Serialize() == loaded.Serialize());
                    Check(Rasterizer.Render(doc).Pixels.SequenceEqual(Rasterizer.Render(loaded).Pixels));
                }
                finally
                {
                    File.Delete(temp);
                }
            });
            Test("Layer metadata, hidden ink, and old document defaults", () =>
            {
                var doc = new LabelDocument();
                var printed = Rasterizer.Render(doc).Pixels.ToArray();
                doc.Objects[0].Name = "Title";
                doc.Objects[0].Locked = true;
                Check(printed.SequenceEqual(Rasterizer.Render(doc).Pixels));
                doc.Objects[0].Visible = false;
                Check(Rasterizer.Render(doc).Pixels.All(p => p == 255));
                var loaded = JsonSerializer.Deserialize<LabelDocument>(doc.Serialize(), Catalog.Json)!;
                Check(loaded.Objects[0].Name == "Title" && loaded.Objects[0].Locked && !loaded.Objects[0].Visible);
                var old = System.Text.Json.Nodes.JsonNode.Parse(new LabelDocument().Serialize())!;
                old.AsObject().Remove("auto_length");
                var item = old["objects"]![0]!.AsObject();
                item.Remove("name");
                item.Remove("locked");
                item.Remove("visible");
                var legacy = JsonSerializer.Deserialize<LabelDocument>(old.ToJsonString(), Catalog.Json)!;
                Check(!legacy.AutoLength && legacy.Objects[0].Visible && !legacy.Objects[0].Locked && legacy.Objects[0].Name == "");
                Check(printed.SequenceEqual(Rasterizer.Render(legacy).Pixels));
            });
            Test("Text caret positions retain Unicode boundaries", () =>
            {
                var item = new LabelObject
                {
                    Text = "A☕😀B",
                    Align = "left"
                };
                using var geometry = new TextGeometry(item, 8);
                foreach (int index in new[]
                {
                    0,
                    1,
                    2,
                    4,
                    5
                }

                )
                {
                    var caret = geometry.Caret(index);
                    Check(geometry.Hit(new Avalonia.Point(caret.X + .01, caret.Y + caret.Height / 2)) == index, "Caret roundtrip " + index);
                }

                Check(geometry.Selection(0, item.Text.Length).Any());
            });
            Test("Reject out-of-bounds and nonfinite objects", () =>
            {
                foreach (var x in new[]
                {
                    -1d,
                    double.NaN,
                    double.PositiveInfinity,
                    39d
                }

                )
                {
                    var doc = new LabelDocument();
                    doc.Objects[0].X = x;
                    bool rejected = false;
                    try
                    {
                        doc.Validate();
                    }
                    catch (InvalidDataException)
                    {
                        rejected = true;
                    }

                    Check(rejected);
                }
            });
            Test("Raster centering on a taller printhead", () =>
            {
                var p = Enumerable.Repeat((byte)255, 80 * 32).ToArray();
                p[8] = 0;
                var (b, _) = Protocol.RasterBuffers(new(80, 32, p), Catalog.Get("e10"));
                var x = 8 - b[0][12];
                Check((b[0][14 + x * 12 + 4] & 1) == 1);
            });
            Test("Session setup and failure cleanup without replay", () =>
            {
                var raster = Rasterizer.Render(new());
                var model = Catalog.Get("e10");
                using var success = new FakeWire();
                Protocol.Print(success, model, raster, 3, 1, false, 3, _ =>
                {
                });
                Check(success.Calls.SequenceEqual(new[] { 0xc9, 0x13, 0xba, 256 }));
                using var failure = new FakeWire
                {
                    Fail = true
                };
                bool threw = false;
                try
                {
                    Protocol.Print(failure, model, raster, 3, 1, false, 3, _ =>
                    {
                    });
                }
                catch (IOException)
                {
                    threw = true;
                }

                Check(threw && failure.Calls.Count(c => c == 256) == 1 && failure.Calls.Count(c => c == 0x14) == 1);
                using var busy = new FakeWire
                {
                    Busy = true
                };
                try
                {
                    Protocol.Print(busy, model, raster, 3, 1, false, 3, _ =>
                    {
                    });
                }
                catch (IOException)
                {
                }

                Check(busy.Calls.Count == 0);
            });
            Test("Physical PDF dimensions", () =>
            {
                var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
                try
                {
                    Rasterizer.Pdf(Rasterizer.Render(new()), 40, 12, file);
                    var content = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(file));
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"/MediaBox\s*\[\s*0\s+0\s+([0-9.]+)\s+([0-9.]+)\s*\]");
                    Check(content.StartsWith("%PDF") && match.Success, content[..Math.Min(content.Length, 2000)]);
                    Check(!content.Contains("/DCTDecode"), "Printer raster must not use lossy JPEG encoding");
                    Check(content.Contains("/Width 320") && content.Contains("/Height 96"), "PDF must embed the original printer raster");
                    Check(Math.Abs(double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 12 * 72 / 25.4) < .01, "PDF width: " + match.Groups[1].Value);
                    Check(Math.Abs(double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) - 40 * 72 / 25.4) < .01);
                }
                finally
                {
                    File.Delete(file);
                }
            });
            Test("Empty selections, surrogate boundaries and clipped text", () =>
            {
                var item = new LabelObject
                {
                    Text = "A😀B",
                    Align = "left"
                };
                using var geometry = new TextGeometry(item, 8);
                Check(!geometry.Selection(1, 1).Any(), "A collapsed selection has no rectangles");
                Check(!geometry.Selection(2, 2).Any(), "A collapsed surrogate selection is empty");
                Check(geometry.Caret(2) == geometry.Caret(1), "Carets never split a surrogate pair");
                Check(geometry.Selection(4, 0).SequenceEqual(geometry.Selection(0, 4)), "Reversed selections use the same bounds");
                var doc = new LabelDocument();
                doc.Objects[0].Text = new string('W', 100);
                doc.Objects[0].Width = 8;
                doc.Objects[0].Height = 4;
                var raster = Rasterizer.Render(doc);
                Check(raster.Pixels.Any(value => value == 0), "Clipped text retains visible ink");
                for (var y = 0; y < raster.Height; y++)
                    for (var x = 0; x < raster.Width; x++)
                        if (x < 16 || x >= 80 || y < 16 || y >= 48)
                            Check(raster.Pixels[y * raster.Width + x] == 255, "Text must stay inside its box: " + x + "," + y);
            });
            Test("Legacy label import remains portable artwork", () =>
            {
                var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".e10label");
                try
                {
                    File.WriteAllText(file, """
                        {"format":"e10-label","version":1,"label":{"text":"Old label","font":"Sans","bold":true,"font_size":30,"length_mm":40,"height_mm":12}}
                        """);
                    var imported = LabelDocument.Load(file);
                    Check(imported.Objects.Count == 1 && imported.Objects[0].Kind == "image");
                    Check(Rasterizer.Render(imported).Pixels.Any(value => value == 0));
                    Check(imported.Serialize().Contains("label-studio"));
                }
                finally
                {
                    File.Delete(file);
                }
            });
            Test("System targets preserve queue names and legacy settings", () =>
            {
                Check(SystemPrinting.IsTarget("system:Office printer", "auto"));
                Check(SystemPrinting.IsTarget("cups:old_queue", "auto"));
                Check(SystemPrinting.IsTarget("Office printer", "cups"));
                Check(!SystemPrinting.IsTarget("AA:BB:CC:DD:EE:FF", "auto"));
                Check(SystemPrinting.QueueName("system:Office printer") == "Office printer");
                Check(SystemPrinting.QueueName("cups:old_queue") == "old_queue");
                Check(SystemPrinting.QueueName("system:name-with-cups:inside") == "name-with-cups:inside");
                if (!OperatingSystem.IsLinux())
                {
                    var rejected = false;
                    try
                    {
                        using var wire = Printers.Connect(Catalog.Get("e10"), "classic", "AA:BB:CC:DD:EE:FF", 1);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        rejected = true;
                    }

                    Check(rejected, "Unsupported direct connections fail before loading native libraries");
                }
            });
            Test("Longest printer prefix wins", () =>
            {
                Check(Catalog.Identify("T0081E2309097700")?.Id == "e10");
                Check(Catalog.Identify("T0117A2410211517")?.Id == "t50m_pro");
                Check(Catalog.Identify("Unknown") == null);
            });
            Console.WriteLine($"{passed} checks passed; {Catalog.Models.Count(m => m.Engine != "cups")} direct profiles verified. No hardware jobs sent.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    sealed class FakeWire : IWire
    {
        public List<int> Calls { get; } = [];

        public bool Fail, Busy, Printing;
        public byte[] Send(byte code, int param = 0, int count = 0)
        {
            Calls.Add(code);
            if (code == 0x13)
                Printing = true;
            if (code == 0x14)
                Printing = false;
            return [];
        }

        public PrinterStatus Status() => new(false, Busy, Printing, 0, []);
        public void Upload(byte[] stream, int speed, bool modern)
        {
            Calls.Add(256);
            if (Fail)
                throw new IOException("Lost connection");
            Printing = false;
        }

        public void Dispose()
        {
        }
    }
}
