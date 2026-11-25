using System.Runtime.InteropServices;
using System.Text.Json;
using SkiaSharp;

namespace LabelStudio;

public static class Rasterizer
{
    public static Raster Render(LabelDocument doc)
    {
        doc.Validate();
        doc.ApplyAutoLength();
        doc.Validate();
        double d = doc.Profile.DotsPerMm;
        using var bitmap = new SKBitmap((int)Math.Round(doc.LengthMm * d), (int)Math.Round(doc.HeightMm * d), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        foreach (var o in doc.Objects)
        {
            if (!o.Visible)
                continue;
            var r = SKRect.Create((float)(o.X * d), (float)(o.Y * d), (float)(o.Width * d), (float)(o.Height * d));
            if (o.Kind == "text")
            {
                TextGeometry.Draw(canvas, o.Text, o.Font, o.FontMm * d, o.Align, r);
            }
            else if (o.Kind == "border")
            {
                using var pen = new SKPaint
                {
                    Color = SKColors.Black,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)(o.StrokeMm * d),
                    IsAntialias = true
                };
                r.Inflate(-pen.StrokeWidth / 2, -pen.StrokeWidth / 2);
                canvas.DrawRoundRect(r, (float)d * .7f, (float)d * .7f, pen);
            }
            else if (o.Image.Length > 0)
            {
                using var image = Decode(o.Image);
                var scale = Math.Min(r.Width / image.Width, r.Height / image.Height);
                var target = SKRect.Create(r.MidX - image.Width * scale / 2, r.MidY - image.Height * scale / 2, image.Width * scale, image.Height * scale);
                using var paint = new SKPaint
                {
                    FilterQuality = SKFilterQuality.High,
                    IsAntialias = true
                };
                canvas.DrawBitmap(image, target, paint);
            }
        }

        canvas.Flush();
        return Threshold(bitmap);
    }

    public static SKBitmap Decode(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        using var codec = SKCodec.Create(new SKMemoryStream(bytes));
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 24000000)
            throw new InvalidDataException("Image is invalid or too large (maximum 24 megapixels).");
        return SKBitmap.Decode(bytes) ?? throw new InvalidDataException("Could not decode artwork.");
    }

    public static string ImportImage(string path)
    {
        if (new FileInfo(path).Length > 12000000)
            throw new InvalidDataException("Choose an image smaller than 12 MB.");
        using var bitmap = Decode(Convert.ToBase64String(File.ReadAllBytes(path)));
        var scale = Math.Min(1d, Math.Min(1600d / bitmap.Width, 1600d / bitmap.Height));
        using var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))), SKFilterQuality.High);
        using var image = SKImage.FromBitmap(resized ?? bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }

    private static Raster Threshold(SKBitmap b)
    {
        var data = new byte[b.ByteCount];
        Marshal.Copy(b.GetPixels(), data, 0, data.Length);
        var pixels = new byte[b.Width * b.Height];
        for (var y = 0; y < b.Height; y++)
            for (var x = 0; x < b.Width; x++)
            {
                var i = y * b.RowBytes + x * 4;
                pixels[y * b.Width + x] = (data[i] * 114 + data[i + 1] * 587 + data[i + 2] * 299) >= 125000 ? (byte)255 : (byte)0;
            }

        return new Raster(b.Width, b.Height, pixels);
    }

    public static Raster Legacy(JsonElement j, LabelDocument doc)
    {
        string S(string n, string fallback = "") => j.TryGetProperty(n, out var p) ? p.GetString() ?? fallback : fallback;
        double d = doc.Profile.DotsPerMm;
        using var b = new SKBitmap((int)Math.Round(doc.LengthMm * d), (int)Math.Round(doc.HeightMm * d), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var c = new SKCanvas(b);
        c.Clear(SKColors.White);
        float left = (float)Math.Max(4, Math.Round(d)), top = (float)Math.Max(3, Math.Round(d * .75));
        float right = b.Width - left, bottom = b.Height - top;
        if (j.TryGetProperty("border", out var border) && border.GetBoolean())
        {
            using var p = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2
            };
            c.DrawRoundRect(new SKRect(4, 4, b.Width - 5, b.Height - 5), 7, 7, p);
            left += 3;
            right -= 3;
        }

        var text = S("text").Trim();
        if (S("image") is { Length: > 0 } source)
        {
            using var image = Decode(source);
            double scale = Math.Min(1, Math.Min((text.Length > 0 ? Math.Min(bottom - top, (right - left) / 3) : right - left) / image.Width, (bottom - top) / image.Height));
            float w = (float)Math.Round(image.Width * scale), h = (float)Math.Round(image.Height * scale);
            using var p = new SKPaint
            {
                FilterQuality = SKFilterQuality.High
            };
            c.DrawBitmap(image, SKRect.Create(text.Length > 0 ? left : (b.Width - w) / 2, (b.Height - h) / 2, w, h), p);
            if (text.Length > 0)
                left += w + 8;
        }

        c.Flush();
        var font = S("font_description");
        if (font.Length == 0)
            font = S("font", "Sans") + " " + S("font_style", j.TryGetProperty("bold", out var bold) && bold.GetBoolean() ? "Bold" : "");
        var size = j.TryGetProperty("font_size", out var fs) ? fs.GetInt32() : 40;
        for (; size >= 4; size--)
        {
            using var layout = TextGeometry.CreateLayout(text, font, size);
            if (layout.WidthIncludingTrailingWhitespace <= right - left && layout.Extent <= bottom - top)
                break;
        }

        if (size < 4)
            throw new InvalidDataException("The old label's text does not fit.");
        TextGeometry.Draw(c, text, font, size, S("align", "center"), new SKRect(left, top, right, bottom));
        c.Flush();
        return Threshold(b);
    }

    public static void Pdf(Raster raster, double lengthMm, double heightMm, string path)
    {
        using var bitmap = raster.Bitmap();
        var pageWidth = (float)(heightMm * 72 / 25.4);
        var pageHeight = (float)(lengthMm * 72 / 25.4);
        // Skia rounds page bounds to its internal DPI grid. A fine grid preserves
        // custom stock dimensions to 0.01 pt; the embedded raster stays unscaled.
        var metadata = SKDocumentPdfMetadata.Default;
        metadata.RasterDpi = 7200;
        using var output = SKDocument.CreatePdf(path, metadata) ?? throw new IOException("Could not create the label PDF.");
        var canvas = output.BeginPage(pageWidth, pageHeight);
        canvas.Translate(0, pageHeight);
        canvas.RotateDegrees(-90);
        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.None,
            IsAntialias = false
        };
        canvas.DrawBitmap(bitmap, SKRect.Create(pageHeight, pageWidth), paint);
        output.EndPage();
        output.Close();
    }
}
