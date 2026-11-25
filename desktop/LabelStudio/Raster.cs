using System.Runtime.InteropServices;
using SkiaSharp;

namespace LabelStudio;

public sealed class Raster(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    // One luminance byte per printer dot. Only 0 and 255 are allowed.
    public byte[] Pixels { get; } = pixels;

    public SKBitmap Bitmap()
    {
        var bitmap = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var data = new byte[Width * Height * 4];
        for (var i = 0; i < Pixels.Length; i++)
        {
            data[i * 4] = data[i * 4 + 1] = data[i * 4 + 2] = Pixels[i];
            data[i * 4 + 3] = 255;
        }

        Marshal.Copy(data, 0, bitmap.GetPixels(), data.Length);
        return bitmap;
    }

    public byte[] PreviewPng()
    {
        // A small optical softening at printer-dot scale approximates the edges of
        // thermal ink. This is a separate display image, never a printable raster.
        using var original = Bitmap();
        int factor = Math.Clamp(2048 / Math.Max(Width, Height), 1, 4);
        using var enlarged = original.Resize(new SKImageInfo(Width * factor, Height * factor), SKFilterQuality.High);
        using var display = new SKBitmap(enlarged.Width, enlarged.Height);
        using var canvas = new SKCanvas(display);
        canvas.Clear(SKColors.White);
        using var blur = SKImageFilter.CreateBlur(.45f * factor, .45f * factor);
        using var paint = new SKPaint
        {
            ImageFilter = blur,
            FilterQuality = SKFilterQuality.High
        };
        canvas.DrawBitmap(enlarged, 0, 0, paint);
        canvas.Flush();
        using var image = SKImage.FromBitmap(display);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public byte[] Png()
    {
        using var b = Bitmap();
        using var image = SKImage.FromBitmap(b);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
