using Avalonia.Controls;
using Avalonia.Layout;

namespace LabelStudio;

public sealed partial class MainWindow
{
    void RefreshInspector()
    {
        refreshSizeFields = null;
        RefreshLayers();
        inspector.IsEnabled = true;
        inspector.Children.Clear();
        if (canvas.Selected < 0 || canvas.Selected >= document.Objects.Count)
        {
            selectionNote.Text = "Click an object on the label.";
            return;
        }

        var o = document.Objects[canvas.Selected];
        selectionNote.Text = o.Locked ? "Locked · unlock in Layers to edit" : $"{o.Kind.ToUpperInvariant()} · {LayerName(o)}";
        inspector.IsEnabled = !o.Locked;
        NumericUpDown PropertyNumber(string name, double value, double min, double max, Action<double> set, double step = .25)
        {
            var n = Number((decimal)min, (decimal)Math.Max(min, max), (decimal)value, (decimal)step);
            n.ValueChanged += (_, _) =>
            {
                if (loading)
                    return;
                PushUndo();
                set((double)(n.Value ?? (decimal)value));
                Changed();
            };
            return n;
        }

        var x = PropertyNumber("X", o.X, 0, (document.AutoLength ? 300 : document.LengthMm) - o.Width, v => o.X = v);
        var y = PropertyNumber("Y", o.Y, 0, document.HeightMm - o.Height, v => o.Y = v);
        var w = PropertyNumber("Width", o.Width, .25, document.LengthMm - o.X, v =>
        {
            o.Width = v;
            x.Maximum = (decimal)(document.LengthMm - v);
        });
        var h = PropertyNumber("Height", o.Height, .25, document.HeightMm - o.Y, v =>
        {
            o.Height = v;
            y.Maximum = (decimal)(document.HeightMm - v);
        });
        w.IsEnabled = !(document.AutoLength && o.Kind == "text" && o.Visible);
        refreshSizeFields = () =>
        {
            if (canvas.Selected < 0 || canvas.Selected >= document.Objects.Count || document.Objects[canvas.Selected] != o)
                return;
            w.Maximum = (decimal)(document.LengthMm - o.X);
            w.Value = (decimal)o.Width;
            x.Maximum = (decimal)((document.AutoLength ? 300 : document.LengthMm) - o.Width);
            x.Value = (decimal)o.X;
        };
        x.ValueChanged += (_, _) => w.Maximum = (decimal)(document.LengthMm - o.X);
        y.ValueChanged += (_, _) => h.Maximum = (decimal)(document.HeightMm - o.Y);
        inspector.Children.Add(TwoFields("X · mm", x, "Y · mm", y));
        inspector.Children.Add(TwoFields("Width · mm", w, "Height · mm", h));
        if (o.Kind == "text")
        {
            inspector.Children.Add(Button("Edit text on label", () => canvas.BeginTextEdit(selectAll: true)));
            var font = new AutoCompleteBox
            {
                Text = o.Font,
                ItemsSource = LabelFonts.Installed.Value,
                FilterMode = AutoCompleteFilterMode.Contains,
                MinimumPrefixLength = 0,
                Watermark = "Search installed fonts and styles…"
            };
            font.TextChanged += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(font.Text) || font.Text == o.Font)
                    return;
                PushUndo();
                o.Font = font.Text;
                Changed();
            };
            inspector.Children.Add(Field("Font and style", font));
            inspector.Children.Add(Field("Text size · mm", PropertyNumber("Font", o.FontMm, .25, 100, v => o.FontMm = v)));
            var align = new ComboBox
            {
                ItemsSource = new[]
                {
                    "left",
                    "center",
                    "right"
                },
                SelectedItem = o.Align,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            align.SelectionChanged += (_, _) =>
            {
                if (align.SelectedItem is not string s)
                    return;
                PushUndo();
                o.Align = s;
                Changed();
            };
            inspector.Children.Add(Field("Alignment", align));
            inspector.Children.Add(Text(document.AutoLength ? "The longest line sets the text width. Artwork and locked or hidden layers keep their bounds and can set the minimum label length." : "Text wraps inside its box. Oversized text is clipped, as shown on the label. Resize the box or reduce text size to fit.", 11, "#74816D"));
        }
        else if (o.Kind == "border")
            inspector.Children.Add(Field("Line width · mm", PropertyNumber("Stroke", o.StrokeMm, .05, 5, v => o.StrokeMm = v, .05)));
        else
            inspector.Children.Add(Text("Artwork keeps its proportions inside this box.", 12, "#74816D"));
        inspector.Children.Add(Row(Button("Center X", () =>
        {
            PushUndo();
            o.X = (document.LengthMm - o.Width) / 2;
            Changed();
            RefreshInspector();
        }), Button("Center Y", () =>
        {
            PushUndo();
            o.Y = (document.HeightMm - o.Height) / 2;
            Changed();
            RefreshInspector();
        })));
        inspector.Children.Add(Row(Button("Send back", () => Layer(-1)), Button("Bring forward", () => Layer(1))));
    }
}
