using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LabelStudio;

public sealed partial class MainWindow
{
    static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
    static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Increment = step,
        FormatString = "0.##",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinWidth = 80
    };
    static TextBlock Text(string text, double size = 13, string color = "#213B2A") => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brush(color),
        TextWrapping = TextWrapping.Wrap
    };
    static StackPanel Column(params Control[] children)
    {
        var p = new StackPanel
        {
            Spacing = 8
        };
        foreach (var c in children)
            p.Children.Add(c);
        return p;
    }

    static StackPanel Row(params Control[] children)
    {
        var p = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        foreach (var c in children)
            p.Children.Add(c);
        return p;
    }

    Button Button(string text, Action action)
    {
        var b = new Button
        {
            Content = text
        };
        b.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        };
        return b;
    }

    Button AsyncButton(string text, Func<Task> action)
    {
        var b = new Button
        {
            Content = text
        };
        b.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        };
        return b;
    }

    static Control Field(string label, Control control) => Column(Text(label, 12, "#61725C"), control);
    static Border Card(Control content, string color = "#FFFFFF", double padding = 18) => new()
    {
        Background = Brush(color),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(padding),
        Child = content
    };
    void BuildLayout()
    {
        var header = new DockPanel
        {
            Margin = new Thickness(24, 16)
        };
        var actions = Row(AsyncButton("New", NewAsync), AsyncButton("Open…", OpenAsync), AsyncButton("Save", () => SaveAsync(false)), AsyncButton("Save as…", () => SaveAsync(true)), AsyncButton("Export PNG…", ExportAsync));
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(Column(Text("Label Studio", 25), Text("Katasymbol & Supvan", 12, "#6E7E67")));
        var toolbar = Row(Button("+ Text", AddText), AsyncButton("+ Artwork…", AddImage), Button("+ Border", AddBorder), Button("Duplicate", Duplicate), Button("Delete", Delete), Button("Undo", Undo), Button("Redo", Redo));
        var viewOptions = new DockPanel();
        var dots = new CheckBox
        {
            Content = "Show printer dots"
        };
        dots.IsCheckedChanged += (_, _) =>
        {
            canvas.ShowDots = dots.IsChecked == true;
            canvas.InvalidateVisual();
        };
        var zoom = new ComboBox
        {
            ItemsSource = new[]
            {
                "Fit",
                "150%",
                "200%",
                "300%"
            },
            SelectedIndex = 0,
            Width = 95
        };
        zoom.SelectionChanged += (_, _) =>
        {
            canvas.Zoom = new[]
            {
                1d,
                1.5,
                2,
                3
            }[Math.Max(0, zoom.SelectedIndex)];
            canvas.InvalidateVisual();
        };
        var view = Row(dots, zoom);
        DockPanel.SetDock(view, Dock.Right);
        viewOptions.Children.Add(view);
        viewOptions.Children.Add(Text("Drag to move · corner to resize · double-click text to edit", 12, "#687964"));
        canvas.MinHeight = 120;
        canvas.Document = document;
        canvas.Selected = 0;
        canvas.SelectionChanged = RefreshInspector;
        canvas.BeforeEdit = PushUndo;
        canvas.Edited = Changed;
        canvas.FinishedEdit = RefreshInspector;
        var workspace = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 15
        };
        workspace.Children.Add(toolbar);
        Grid.SetRow(viewOptions, 1);
        workspace.Children.Add(viewOptions);
        var paperCard = Card(canvas, "#E8ECE7", 0);
        Grid.SetRow(paperCard, 2);
        workspace.Children.Add(paperCard);
        var footer = Column(Text("Preview, export, and print use the same printer dots.", 12, "#687964"), PrinterCard());
        Grid.SetRow(footer, 3);
        workspace.Children.Add(footer);
        var sidebar = Column(Text("LABEL", 11, "#6D8067"), Field("Printer model", models), modelNote, TwoFields("Length · mm", length, "Height · mm", height), autoLength, lengthNote, new Separator(), LayersPanel(), new Separator(), Text("SELECTED OBJECT", 11, "#6D8067"), selectionNote, inspector);
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            ColumnSpacing = 24,
            Margin = new Thickness(24, 0, 24, 22)
        };
        body.Children.Add(Card(new ScrollViewer { Content = sidebar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }));
        Grid.SetColumn(workspace, 1);
        body.Children.Add(workspace);
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        root.Children.Add(header);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = root;
    }

    static Control TwoFields(string a, Control x, string b, Control y)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8
        };
        grid.Children.Add(Field(a, x));
        var right = Field(b, y);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    Control PrinterCard()
    {
        var find = AsyncButton("Find printer", async () => await Work(async () =>
        {
            var found = await Task.Run(() => Printers.Discover(Progress));
            devices.ItemsSource = found;
            if (found.Count == 1)
                devices.SelectedIndex = 0;
            status.Text = found.Count == 0 ? "No printers found. Turn yours on and try again." : $"Found {found.Count} printer(s). Choose yours above.";
        }));
        var pair = AsyncButton("Pair", async () => await Work(async () =>
        {
            var address = target.Text?.Trim() ?? "";
            status.Text = await Task.Run(() => Printers.Pair(address));
            SaveSettings();
        }));
        var check = AsyncButton("Check status", CheckAsync);
        connectionButtons.AddRange([find, pair, check]);
        pair.IsVisible = OperatingSystem.IsLinux();
        channel.IsEnabled = OperatingSystem.IsLinux();
        var options = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 12
        };
        var fields = new[]
        {
            Field("Density", density),
            Field("Copies", copies),
            Field("Bluetooth channel", channel),
            Field("Gap · mm", gap)
        };
        for (int i = 0; i < fields.Length; i++)
        {
            Grid.SetColumn(fields[i], i);
            options.Children.Add(fields[i]);
        }

        var settings = new Expander
        {
            Header = "Print settings",
            Content = Column(options, dieCut),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var printerRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        printerRow.Children.Add(devices);
        Grid.SetColumn(find, 1);
        printerRow.Children.Add(find);
        var addressRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 8
        };
        addressRow.Children.Add(target);
        Grid.SetColumn(pair, 1);
        addressRow.Children.Add(pair);
        Grid.SetColumn(check, 2);
        addressRow.Children.Add(check);
        var bottom = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 16
        };
        bottom.Children.Add(status);
        Grid.SetColumn(print, 1);
        bottom.Children.Add(print);
        return Card(Column(Text("YOUR PRINTER", 11, "#6D8067"), connections, printerRow, addressRow, settings, bottom), "#E9EFE5");
    }
}
