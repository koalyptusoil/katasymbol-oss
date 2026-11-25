using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LabelStudio;

public sealed partial class MainWindow
{
    void PushUndo()
    {
        if (loading)
            return;
        var state = document.Serialize();
        if (state == lastHistory)
            return;
        undo.Push(state);
        lastHistory = state;
        redo.Clear();
        if (undo.Count > 100)
        {
            var keep = undo.Take(100).Reverse().ToArray();
            undo.Clear();
            foreach (var s in keep)
                undo.Push(s);
        }
    }

    void Restore(Stack<string> from, Stack<string> to)
    {
        canvas.EndTextEdit();
        if (from.Count == 0)
            return;
        to.Push(document.Serialize());
        document = JsonSerializer.Deserialize<LabelDocument>(from.Pop(), Catalog.Json)!;
        lastHistory = null;
        canvas.Selected = Math.Min(canvas.Selected, document.Objects.Count - 1);
        RefreshDocument();
        Changed();
    }

    void Undo() => Restore(undo, redo);
    void Redo() => Restore(redo, undo);
    void Changed()
    {
        bool limited = document.ApplyAutoLength();
        loading = true;
        try
        {
            length.Value = (decimal)document.LengthMm;
            length.IsEnabled = !document.AutoLength;
            lengthNote.IsVisible = document.AutoLength;
            lengthNote.Text = limited ? "Maximum 300 mm reached; text may wrap. Check the preview." : "Grows as you type; keeps text size. Adds 2 mm after text. Use continuous tape.";
            refreshSizeFields?.Invoke();
        }
        finally
        {
            loading = false;
        }

        dirty = true;
        Title = "Label Studio · " + (path == null ? "Untitled" : Path.GetFileName(path)) + " *";
        Render();
        RefreshLayers();
        if (canvas.Selected >= 0 && canvas.Selected < document.Objects.Count)
        {
            var o = document.Objects[canvas.Selected];
            selectionNote.Text = o.Locked ? "Locked · unlock in Layers to edit" : $"{o.Kind.ToUpperInvariant()} · {LayerName(o)}";
        }
    }

    void Render()
    {
        try
        {
            canvas.UpdateRaster();
            UpdatePrint();
        }
        catch (Exception ex)
        {
            print.IsEnabled = false;
            status.Text = ex.Message;
        }
    }

    void UpdatePrint() => print.IsEnabled = !busy && canvas.Raster != null && !string.IsNullOrWhiteSpace(target.Text);
    void AddText()
    {
        canvas.EndTextEdit();
        PushUndo();
        document.Objects.Add(new()
        {
            X = 1,
            Y = 1,
            Width = document.LengthMm - 2,
            Height = document.HeightMm - 2,
            FontMm = Math.Min(4, document.HeightMm / 2),
            Text = "Your text"
        });
        canvas.Selected = document.Objects.Count - 1;
        Changed();
        RefreshInspector();
        canvas.BeginTextEdit(selectAll: true);
    }

    async Task AddImage()
    {
        canvas.EndTextEdit();
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Add artwork",
            AllowMultiple = false,
            FileTypeFilter = [new("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"] }]
        });
        var file = files.FirstOrDefault()?.TryGetLocalPath();
        if (file == null)
            return;
        var image = Rasterizer.ImportImage(file);
        PushUndo();
        document.Objects.Add(new()
        {
            Kind = "image",
            Image = image,
            X = 1,
            Y = 1,
            Width = Math.Min(document.HeightMm - 2, document.LengthMm - 2),
            Height = document.HeightMm - 2
        });
        canvas.Selected = document.Objects.Count - 1;
        Changed();
        RefreshInspector();
    }

    void AddBorder()
    {
        canvas.EndTextEdit();
        PushUndo();
        document.Objects.Insert(0, new()
        {
            Kind = "border",
            X = .5,
            Y = .5,
            Width = document.LengthMm - 1,
            Height = document.HeightMm - 1
        });
        canvas.Selected = 0;
        Changed();
        RefreshInspector();
    }

    void Duplicate()
    {
        canvas.EndTextEdit();
        if (canvas.Selected < 0)
            return;
        PushUndo();
        var o = document.Objects[canvas.Selected].Clone();
        o.Locked = false;
        o.X = Math.Min(o.X + 1, document.LengthMm - o.Width);
        o.Y = Math.Min(o.Y + 1, document.HeightMm - o.Height);
        document.Objects.Add(o);
        canvas.Selected = document.Objects.Count - 1;
        Changed();
        RefreshInspector();
    }

    void Delete()
    {
        canvas.EndTextEdit();
        if (canvas.Selected < 0)
            return;
        if (document.Objects[canvas.Selected].Locked)
        {
            status.Text = "Unlock this layer before deleting it.";
            return;
        }

        PushUndo();
        document.Objects.RemoveAt(canvas.Selected);
        canvas.Selected = Math.Min(canvas.Selected, document.Objects.Count - 1);
        Changed();
        RefreshInspector();
    }

    void Layer(int delta)
    {
        canvas.EndTextEdit();
        int i = canvas.Selected, j = i + delta;
        if (i < 0 || j < 0 || j >= document.Objects.Count)
            return;
        PushUndo();
        (document.Objects[i], document.Objects[j]) = (document.Objects[j], document.Objects[i]);
        canvas.Selected = j;
        Changed();
        RefreshInspector();
    }
}
