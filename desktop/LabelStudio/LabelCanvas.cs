using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Layout;

namespace LabelStudio;

public sealed partial class LabelCanvas : Decorator
{
    public LabelDocument Document { get; set; } = new();
    public int Selected { get; set; } = 0;
    public bool ShowDots
    {
        get; set;
    }

    double zoom = 1;
    public double Zoom
    {
        get => zoom;
        set
        {
            zoom = value;
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    InlineTextBox? editor;
    LabelObject? editingObject;
    bool editHasHistory, caretVisible = true;
    readonly DispatcherTimer caretTimer;
    public bool IsEditingText => editor != null;

    public Action? SelectionChanged, BeforeEdit, Edited, FinishedEdit;
    Bitmap? bitmap, smoothBitmap;
    Rect paper;
    Point start;
    LabelObject? original;
    bool dragging, resizing;
    double scale;
    public Raster? Raster
    {
        get; private set;
    }

    public LabelCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        caretTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        caretTimer.Tick += (_, _) =>
        {
            caretVisible = !caretVisible;
            InvalidateVisual();
        };
        DetachedFromVisualTree += (_, _) => EndTextEdit();
    }

    void Geometry(Size size)
    {
        scale = Math.Max(.05, Math.Min((size.Width - 96) / Document.LengthMm, (size.Height - 110) / Document.HeightMm) * Zoom);
        paper = new Rect((size.Width - Document.LengthMm * scale) / 2, (size.Height - Document.HeightMm * scale) / 2, Document.LengthMm * scale, Document.HeightMm * scale);
    }

    protected override Size MeasureOverride(Size available)
    {
        editor?.Measure(available);
        return new Size(double.IsFinite(available.Width) ? available.Width : 400, double.IsFinite(available.Height) ? available.Height : 250);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Geometry(finalSize);
        if (editor != null && editingObject != null)
        {
            editor.FontSize = Math.Max(1, editingObject.FontMm * scale);
            editor.Arrange(ObjectRect(editingObject));
        }

        return finalSize;
    }

    public bool BeginTextEdit(bool selectAll = false, Point? at = null)
    {
        if (Selected < 0 || Selected >= Document.Objects.Count)
            return false;
        var o = Document.Objects[Selected];
        if (o.Kind != "text" || o.Locked || !o.Visible)
            return false;
        if (editingObject == o && editor != null)
        {
            editor.Focus();
            if (selectAll)
                editor.SelectAll();
            return true;
        }

        EndTextEdit();
        editingObject = o;
        editHasHistory = false;
        editor = new InlineTextBox(this)
        {
            Name = "CanvasTextEditor",
            Text = o.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = 10000,
            IsUndoEnabled = false,
            Background = Brushes.Transparent,
            Foreground = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            CaretBrush = Brushes.Transparent,
            SelectionBrush = Brushes.Transparent,
            SelectionForegroundBrush = Brushes.Transparent,
            TextAlignment = o.Align == "left" ? TextAlignment.Left : o.Align == "right" ? TextAlignment.Right : TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            FontSize = Math.Max(1, o.FontMm * scale)
        };
        foreach (var key in new[]
        {
            "TextControlBackground",
            "TextControlBackgroundFocused",
            "TextControlBackgroundPointerOver",
            "TextControlBorderBrushFocused"
        }

        )
            editor.Resources[key] = Brushes.Transparent;
        editor.TextChanged += (_, _) => ApplyText();
        editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
            {
                caretVisible = true;
                InvalidateVisual();
            }
        };
        editor.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (editor != null && !editor.IsKeyboardFocusWithin)
                EndTextEdit();
        });
        Child = editor;
        InvalidateMeasure();
        UpdateLayout();
        editor.Focus();
        if (selectAll)
            editor.SelectAll();
        else
        {
            var index = at is { } p ? TextHit(p) : o.Text.Length;
            editor.CaretIndex = index;
            editor.SelectionStart = editor.SelectionEnd = index;
        }

        caretVisible = true;
        caretTimer.Start();
        InvalidateVisual();
        return true;
    }

    void ApplyText()
    {
        if (editor == null || editingObject == null || editor.Text == editingObject.Text)
            return;
        if (!editHasHistory)
        {
            BeforeEdit?.Invoke();
            editHasHistory = true;
        }

        editingObject.Text = editor.Text ?? "";
        Edited?.Invoke();
        InvalidateVisual();
    }

    public void EndTextEdit()
    {
        if (editor == null)
            return;
        ApplyText();
        var old = editor;
        editor = null;
        editingObject = null;
        caretTimer.Stop();
        Child = null;
        FinishedEdit?.Invoke();
        InvalidateVisual();
    }

    int TextHit(Point relative)
    {
        if (editingObject == null)
            return 0;
        using var geometry = new TextGeometry(editingObject, Document.Profile.DotsPerMm);
        return geometry.Hit(relative * (Document.Profile.DotsPerMm / scale));
    }

    void DrawTextEditing(DrawingContext context)
    {
        if (editor == null || editingObject == null)
            return;
        var box = ObjectRect(editingObject);
        double ratio = scale / Document.Profile.DotsPerMm;
        using var geometry = new TextGeometry(editingObject, Document.Profile.DotsPerMm);
        using (context.PushClip(box))
        {
            foreach (var rect in geometry.Selection(editor.SelectionStart, editor.SelectionEnd))
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(75, 40, 140, 85)), null, new Rect(box.X + rect.X * ratio, box.Y + rect.Y * ratio, rect.Width * ratio, rect.Height * ratio));
            if (caretVisible)
            {
                var caret = geometry.Caret(editor.CaretIndex);
                var x = box.X + caret.X * ratio;
                context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#237345")), 1.5), new Point(x, box.Y + caret.Y * ratio), new Point(x, box.Y + caret.Bottom * ratio));
            }
        }
    }

    public void UpdateRaster()
    {
        Raster = null;
        var raster = Rasterizer.Render(Document);
        var next = new Bitmap(new MemoryStream(raster.Png()));
        var smooth = new Bitmap(new MemoryStream(raster.PreviewPng()));
        bitmap?.Dispose();
        smoothBitmap?.Dispose();
        bitmap = next;
        smoothBitmap = smooth;
        Raster = raster;
        InvalidateArrange();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#E8ECE7")), null, Bounds.WithX(0).WithY(0));
        Geometry(Bounds.Size);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#CCD4CC")), null, paper.Translate(new Vector(0, 5)), 2, 2);
        context.DrawRectangle(Brushes.White, null, paper);
        if (bitmap != null)
        {
            // Interpolation affects only presentation; the cached printer raster remains untouched.
            RenderOptions.SetBitmapInterpolationMode(this, ShowDots ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
            var display = ShowDots ? bitmap : smoothBitmap ?? bitmap;
            context.DrawImage(display, new Rect(display.Size), paper);
        }

        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#B4BFB3")), 1), paper);
        var rulerPen = new Pen(new SolidColorBrush(Color.Parse("#9BAA9A")), 1);
        for (var mm = 0; mm <= Document.LengthMm; mm += 5)
        {
            var x = paper.X + mm * scale;
            context.DrawLine(rulerPen, new Point(x, paper.Y - 9), new Point(x, paper.Y - (mm % 10 == 0 ? 18 : 13)));
            if (mm % 10 == 0)
                Text(context, mm.ToString(), new Point(x - 6, paper.Y - 35), 10, "#687966");
        }

        for (var mm = 0; mm <= Document.HeightMm; mm += 5)
        {
            var y = paper.Y + mm * scale;
            context.DrawLine(rulerPen, new Point(paper.X - 9, y), new Point(paper.X - 16, y));
            Text(context, mm.ToString(), new Point(paper.X - 36, y - 7), 10, "#687966");
        }

        if (Selected >= 0 && Selected < Document.Objects.Count && Document.Objects[Selected].Visible)
        {
            var r = ObjectRect(Document.Objects[Selected]);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#237345")), 1.5, dashStyle: DashStyle.Dash), r);
            var handle = new Rect(r.Right - 5, r.Bottom - 5, 10, 10);
            if (!Document.Objects[Selected].Locked && !IsEditingText)
                context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.Parse("#237345")), 2), handle, 2, 2);
        }

        DrawTextEditing(context);
        Text(context, $"{Document.LengthMm:g} × {Document.HeightMm:g} mm   ·   {Raster?.Width} × {Raster?.Height} dots", new Point(Math.Max(12, paper.X), paper.Bottom + 20), 12, "#52644F");
    }

    static void Text(DrawingContext c, string value, Point p, double size, string color)
    {
        var text = new FormattedText(value, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter, sans-serif"), size, new SolidColorBrush(Color.Parse(color)));
        c.DrawText(text, p);
    }

    Rect ObjectRect(LabelObject o) => new(paper.X + o.X * scale, paper.Y + o.Y * scale, o.Width * scale, o.Height * scale);
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is InlineTextBox || IsEditingText && e.Source != this)
            return;
        EndTextEdit();
        Focus();
        var point = e.GetPosition(this);
        if (Selected >= 0 && Selected < Document.Objects.Count)
        {
            var r = ObjectRect(Document.Objects[Selected]);
            resizing = Document.Objects[Selected].Visible && !Document.Objects[Selected].Locked && new Rect(r.Right - 9, r.Bottom - 9, 18, 18).Contains(point);
        }

        if (!resizing)
        {
            Selected = -1;
            for (int i = Document.Objects.Count - 1; i >= 0; i--)
                if (Document.Objects[i].Visible && !Document.Objects[i].Locked && ObjectRect(Document.Objects[i]).Contains(point))
                {
                    Selected = i;
                    break;
                }
        }

        SelectionChanged?.Invoke();
        InvalidateVisual();
        if (Selected < 0)
            return;
        if (e.ClickCount == 2 && Document.Objects[Selected].Kind == "text")
        {
            BeginTextEdit(at: new Point(point.X - ObjectRect(Document.Objects[Selected]).X, point.Y - ObjectRect(Document.Objects[Selected]).Y));
            e.Handled = true;
            return;
        }

        BeforeEdit?.Invoke();
        original = Document.Objects[Selected].Clone();
        start = point;
        dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging || original == null || Selected < 0)
            return;
        var point = e.GetPosition(this);
        var dx = (point.X - start.X) / scale;
        var dy = (point.Y - start.Y) / scale;
        var o = Document.Objects[Selected];
        double Snap(double v) => (e.KeyModifiers & KeyModifiers.Alt) != 0 ? v : Math.Round(v * 4) / 4;
        if (resizing)
        {
            o.Width = Math.Clamp(Snap(original.Width + dx), .25, Document.LengthMm - o.X);
            o.Height = Math.Clamp(Snap(original.Height + dy), .25, Document.HeightMm - o.Y);
        }
        else
        {
            o.X = Math.Clamp(Snap(original.X + dx), 0, Document.LengthMm - o.Width);
            o.Y = Math.Clamp(Snap(original.Y + dy), 0, Document.HeightMm - o.Height);
        }

        Edited?.Invoke();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging)
            return;
        dragging = resizing = false;
        original = null;
        e.Pointer.Capture(null);
        FinishedEdit?.Invoke();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsEditingText || Selected < 0 || Selected >= Document.Objects.Count || Document.Objects[Selected].Locked)
            return;
        if (e.Key is Key.Enter or Key.F2)
        {
            BeginTextEdit();
            e.Handled = true;
            return;
        }

        var o = Document.Objects[Selected];
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : .25;
        double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
        if (dx == 0 && dy == 0)
            return;
        BeforeEdit?.Invoke();
        o.X = Math.Clamp(o.X + dx, 0, Document.LengthMm - o.Width);
        o.Y = Math.Clamp(o.Y + dy, 0, Document.HeightMm - o.Height);
        Edited?.Invoke();
        FinishedEdit?.Invoke();
        e.Handled = true;
    }
}
