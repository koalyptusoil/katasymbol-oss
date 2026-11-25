using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Skia.Helpers;
using SkiaSharp;

namespace LabelStudio;

// Printing, automatic length, caret placement and selection share printer-dot
// metrics. The transparent TextBox supplies text input, clipboard and IME.
internal sealed class TextGeometry : IDisposable
{
    private readonly TextLayout layout;
    private readonly string text;
    public double OffsetY
    {
        get;
    }
    public double NaturalWidthMm
    {
        get;
    }

    public TextGeometry(LabelObject item, double dotsPerMm)
    {
        text = item.Text;
        using var natural = CreateLayout(item.Text, item.Font, item.FontMm * dotsPerMm);
        // One spare dot prevents rounding from wrapping the final glyph.
        NaturalWidthMm = (Math.Ceiling(natural.WidthIncludingTrailingWhitespace + Math.Max(0, natural.OverhangTrailing)) + 1) / dotsPerMm;
        layout = CreateLayout(item.Text, item.Font, item.FontMm * dotsPerMm, item.Width * dotsPerMm, item.Align);
        OffsetY = VerticalOffset(layout, item.Height * dotsPerMm, text.Length == 0);
    }

    internal static TextLayout CreateLayout(string text, string font, double size,
        double width = double.PositiveInfinity, string align = "left")
    {
        var alignment = align switch
        {
            "right" => TextAlignment.Right,
            "center" => TextAlignment.Center,
            _ => TextAlignment.Left
        };
        return new TextLayout(text, LabelFonts.Resolve(font), size, Brushes.Black,
            textAlignment: alignment,
            textWrapping: double.IsFinite(width) ? TextWrapping.Wrap : TextWrapping.NoWrap,
            maxWidth: Math.Max(1, width));
    }

    private static double VerticalOffset(TextLayout textLayout, double height, bool empty)
    {
        if (empty)
            return (height - textLayout.Height) / 2;
        var inkTop = textLayout.Height + textLayout.OverhangAfter - textLayout.Extent;
        return (height - textLayout.Extent) / 2 - inkTop;
    }

    internal static void Draw(SKCanvas canvas, string text, string font, double size, string align, SKRect bounds)
    {
        using var textLayout = CreateLayout(text, font, size, bounds.Width, align);
        var clip = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        var origin = new Point(bounds.Left, bounds.Top + VerticalOffset(textLayout, bounds.Height, text.Length == 0));
        var visual = new TextVisual(textLayout, origin, clip);
        var visualSize = new Size(bounds.Right, bounds.Bottom);
        visual.Measure(visualSize);
        visual.Arrange(new Rect(visualSize));
        // The public bridge renders the same TextLayout onto our printer bitmap.
        // Render the whole visual: the bridge translates a cropped rectangle to
        // the origin, which would shift text away from its document coordinates.
        canvas.Save();
        try
        {
            DrawingContextHelper.RenderAsync(canvas, visual, new Rect(visualSize), new Vector(96, 96)).GetAwaiter().GetResult();
        }
        finally { canvas.Restore(); }
    }

    private sealed class TextVisual(TextLayout textLayout, Point origin, Rect clip) : Control
    {
        public override void Render(DrawingContext context)
        {
            using (context.PushClip(clip))
                textLayout.Draw(context, origin);
        }
    }

    private int NormalizeIndex(int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index > 0 && index < text.Length && char.IsLowSurrogate(text[index]))
            index--;
        return index;
    }

    public Rect Caret(int index)
    {
        var rect = layout.HitTestTextPosition(NormalizeIndex(index));
        return new Rect(rect.X, rect.Y + OffsetY, 0, rect.Height);
    }

    public int Hit(Point dots) => NormalizeIndex(layout.HitTestPoint(new Point(dots.X, dots.Y - OffsetY)).TextPosition);
    public IEnumerable<Rect> Selection(int start, int end)
    {
        (start, end) = (NormalizeIndex(Math.Min(start, end)), NormalizeIndex(Math.Max(start, end)));
        if (start == end)
            return [];
        return layout.HitTestTextRange(start, end - start).Select(rect => rect.Translate(new Vector(0, OffsetY)));
    }

    public void Dispose() => layout.Dispose();
}
