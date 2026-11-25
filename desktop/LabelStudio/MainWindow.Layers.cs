using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace LabelStudio;

public sealed partial class MainWindow
{
    readonly ListBox layerList = new()
    {
        Name = "LayerList",
        MinHeight = 95,
        MaxHeight = 240,
        Padding = new Thickness(0),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
    };
    readonly List<LayerRow> layerRows = [];
    bool refreshingLayers;
    LayerRow? draggedLayer, dropLayer;
    Point dragStart;
    static string LayerName(LabelObject o) => o.Name.Length > 0 ? o.Name : o.Kind == "text" ? (o.Text.Split('\n')[0].Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, 40)] : "Empty text") : o.Kind == "image" ? "Artwork" : "Border";
    Control LayersPanel()
    {
        layerList.SelectionChanged += (_, _) =>
        {
            if (!refreshingLayers && layerList.SelectedItem is LayerRow row)
                SelectLayer(row.Object);
        };
        layerList.PointerMoved += (_, e) =>
        {
            if (draggedLayer == null || Math.Abs(e.GetPosition(layerList).X - dragStart.X) + Math.Abs(e.GetPosition(layerList).Y - dragStart.Y) < 4)
                return;
            dropLayer = layerList.GetVisualAt(e.GetPosition(layerList))?.GetSelfAndVisualAncestors().OfType<LayerRow>().FirstOrDefault();
            foreach (var row in layerRows)
            {
                row.BorderThickness = new Thickness(0, 1, 0, 0);
                row.BorderBrush = row == dropLayer ? Brush("#2F6844") : Brushes.Transparent;
            }

            e.Handled = true;
        };
        layerList.PointerReleased += (_, e) =>
        {
            if (draggedLayer == null)
                return;
            var source = draggedLayer.Object;
            var destination = dropLayer?.Object;
            draggedLayer = dropLayer = null;
            e.Pointer.Capture(null);
            foreach (var row in layerRows)
                row.BorderBrush = Brushes.Transparent;
            if (destination != null)
                ReorderLayer(source, destination);
            e.Handled = true;
        };
        var up = Button("↑", () => Layer(1));
        ToolTip.SetTip(up, "Move layer forward");
        var down = Button("↓", () => Layer(-1));
        ToolTip.SetTip(down, "Move layer back");
        var duplicate = Button("Copy", Duplicate);
        ToolTip.SetTip(duplicate, "Duplicate layer");
        var delete = Button("Delete", Delete);
        return Column(Text("LAYERS", 11, "#6D8067"), Text("Front to back · drag the type icon to reorder", 11, "#74816D"), layerList, Row(up, down, duplicate, delete));
    }

    void SelectLayer(LabelObject item)
    {
        var index = document.Objects.IndexOf(item);
        if (index < 0 || canvas.Selected == index)
            return;
        canvas.EndTextEdit();
        canvas.Selected = index;
        canvas.InvalidateVisual();
        RefreshInspector();
    }

    void ReorderLayer(LabelObject source, LabelObject destination)
    {
        int from = document.Objects.IndexOf(source), to = document.Objects.IndexOf(destination);
        if (from < 0 || to < 0 || from == to)
            return;
        canvas.EndTextEdit();
        PushUndo();
        document.Objects.RemoveAt(from);
        document.Objects.Insert(to, source);
        canvas.Selected = to;
        Changed();
        RefreshInspector();
    }

    void RefreshLayers()
    {
        refreshingLayers = true;
        try
        {
            var objects = document.Objects.AsEnumerable().Reverse().ToArray();
            if (!layerRows.Select(r => r.Object).SequenceEqual(objects))
            {
                layerRows.Clear();
                foreach (var item in objects)
                    layerRows.Add(CreateLayerRow(item));
                layerList.ItemsSource = layerRows.ToArray();
            }

            foreach (var row in layerRows)
            {
                row.Background = canvas.Selected >= 0 && canvas.Selected < document.Objects.Count && row.Object == document.Objects[canvas.Selected] ? Brush("#DCEBDD") : Brushes.Transparent;
                row.Visibility.IsChecked = row.Object.Visible;
                row.Lock.IsChecked = row.Object.Locked;
                if (!row.NameEditor.IsKeyboardFocusWithin && row.NameEditor.Text != LayerName(row.Object))
                    row.NameEditor.Text = LayerName(row.Object);
                row.NameEditor.Foreground = Brush(row.Object.Visible ? "#213B2A" : "#899285");
                ToolTip.SetTip(row.Visibility, row.Object.Visible ? "Hide layer (also excludes it from print)" : "Show layer");
                ToolTip.SetTip(row.Lock, row.Object.Locked ? "Unlock layer" : "Lock layer");
            }

            layerList.SelectedItem = canvas.Selected >= 0 && canvas.Selected < document.Objects.Count ? layerRows.FirstOrDefault(r => r.Object == document.Objects[canvas.Selected]) : null;
        }
        finally
        {
            refreshingLayers = false;
        }
    }

    LayerRow CreateLayerRow(LabelObject item)
    {
        var row = new LayerRow(item);
        row.Visibility.IsCheckedChanged += (_, _) =>
        {
            if (refreshingLayers)
                return;
            var visible = row.Visibility.IsChecked == true;
            if (item.Visible == visible)
                return;
            canvas.EndTextEdit();
            PushUndo();
            item.Visible = visible;
            Changed();
            RefreshInspector();
        };
        row.Lock.IsCheckedChanged += (_, _) =>
        {
            if (refreshingLayers)
                return;
            var locked = row.Lock.IsChecked == true;
            if (item.Locked == locked)
                return;
            canvas.EndTextEdit();
            PushUndo();
            item.Locked = locked;
            Changed();
            RefreshInspector();
        };
        row.NameEditor.GotFocus += (_, _) => SelectLayer(item);
        row.NameEditor.TextChanged += (_, _) =>
        {
            if (refreshingLayers || !row.NameEditor.IsKeyboardFocusWithin || row.NameEditor.Text == LayerName(item))
                return;
            PushUndo();
            item.Name = row.NameEditor.Text ?? "";
            Changed();
        };
        row.NameEditor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                canvas.Focus();
                e.Handled = true;
            }
        };
        row.Icon.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(layerList).Properties.IsLeftButtonPressed)
                return;
            SelectLayer(item);
            draggedLayer = row;
            dropLayer = null;
            dragStart = e.GetPosition(layerList);
            e.Pointer.Capture(row.Icon);
            e.Handled = true;
        };
        row.DoubleTapped += (_, e) =>
        {
            if (e.Source is TextBox || row.NameEditor.IsKeyboardFocusWithin)
                return;
            SelectLayer(item);
            canvas.BeginTextEdit(selectAll: true);
            e.Handled = true;
        };
        return row;
    }

    sealed class LayerRow : ListBoxItem
    {
        protected override Type StyleKeyOverride => typeof(ListBoxItem);
        public LabelObject Object
        {
            get;
        }
        public ToggleButton Visibility
        {
            get;
        } = new()
        {
            Name = "VisibilityToggle",
            Content = "◉",
            Width = 28,
            Padding = new Thickness(2),
            Background = Brushes.Transparent
        };
        public ToggleButton Lock
        {
            get;
        } = new()
        {
            Name = "LockToggle",
            Content = "Lock",
            FontSize = 10,
            Width = 40,
            Padding = new Thickness(2),
            Background = Brushes.Transparent
        };
        public TextBox NameEditor
        {
            get;
        }
        public TextBlock Icon
        {
            get;
        }

        public LayerRow(LabelObject item)
        {
            Object = item;
            Padding = new Thickness(4);
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            NameEditor = new TextBox
            {
                Name = "LayerName",
                Text = LayerName(item),
                MaxLength = 128,
                FontSize = 12,
                Padding = new Thickness(3, 4),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                MinWidth = 25
            };
            Icon = new TextBlock
            {
                Text = item.Kind == "text" ? "T" : item.Kind == "image" ? "▧" : "▱",
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.SizeAll)
            };
            ToolTip.SetTip(Icon, "Drag to reorder this layer");
            ToolTip.SetTip(NameEditor, "Rename layer");
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("28,24,*,40"),
                ColumnSpacing = 2
            };
            Control[] cells = [Visibility, Icon, NameEditor, Lock];
            for (int i = 0; i < cells.Length; i++)
            {
                Grid.SetColumn(cells[i], i);
                grid.Children.Add(cells[i]);
            }

            Content = grid;
        }
    }
}
