using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace LabelStudio;

public sealed partial class MainWindow
{
    void Replace(LabelDocument next, string? file)
    {
        canvas.EndTextEdit();
        document = next;
        path = file;
        dirty = false;
        undo.Clear();
        redo.Clear();
        lastHistory = null;
        canvas.Selected = document.Objects.Count > 0 ? 0 : -1;
        RefreshDocument();
        Render();
        Title = "Label Studio · " + (path == null ? "Untitled" : Path.GetFileName(path));
        status.Text = file?.EndsWith(".e10label", StringComparison.OrdinalIgnoreCase) == true ? "Old label imported as artwork to retain its layout. Add new text or images, then save as .labelstudio." : "Ready.";
    }

    async Task NewAsync()
    {
        if (!await CanReplace())
            return;
        var next = new LabelDocument
        {
            ModelId = document.ModelId,
            HeightMm = Math.Min(12, document.Profile.HeadDots / document.Profile.DotsPerMm)
        };
        next.Objects[0].Y = 1;
        next.Objects[0].Height = next.HeightMm - 2;
        Replace(next, null);
    }

    async Task OpenAsync()
    {
        if (!await CanReplace())
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Open label",
            AllowMultiple = false,
            FileTypeFilter = [new("Label designs") { Patterns = ["*.labelstudio", "*.e10label"] }]
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is string file)
            Replace(LabelDocument.Load(file), file);
    }

    async Task<bool> SaveAsync(bool saveAs)
    {
        canvas.EndTextEdit();
        string? file = path;
        if (file == null || saveAs || file.EndsWith(".e10label", StringComparison.OrdinalIgnoreCase))
        {
            var selected = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = "Save label",
                SuggestedFileName = file == null ? "Untitled.labelstudio" : Path.GetFileNameWithoutExtension(file) + ".labelstudio",
                DefaultExtension = "labelstudio",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new("Label Studio design") { Patterns = ["*.labelstudio"] }]
            });
            file = selected?.TryGetLocalPath();
        }

        if (file == null)
            return false;
        document.Save(file);
        path = file;
        dirty = false;
        Title = "Label Studio · " + Path.GetFileName(file);
        status.Text = "Saved " + Path.GetFileName(file);
        return true;
    }

    async Task ExportAsync()
    {
        canvas.EndTextEdit();
        var raster = Rasterizer.Render(document);
        var selected = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Export printer raster",
            SuggestedFileName = "Label.png",
            DefaultExtension = "png",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("PNG image") { Patterns = ["*.png"] }]
        });
        if (selected?.TryGetLocalPath() is string file)
        {
            File.WriteAllBytes(file, raster.Png());
            status.Text = "Exported the original printer dots to " + Path.GetFileName(file);
        }
    }

    async Task<bool> CanReplace()
    {
        canvas.EndTextEdit();
        if (!dirty)
            return true;
        var result = new TaskCompletionSource<string>();
        var dialog = new Window
        {
            Title = "Save your label?",
            Width = 410,
            Height = 190,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        Button Choice(string label, string value)
        {
            var b = new Button
            {
                Content = label
            };
            b.Click += (_, _) =>
            {
                result.TrySetResult(value);
                dialog.Close();
            };
            return b;
        }

        dialog.Content = new Border
        {
            Padding = new Thickness(24),
            Child = Column(Text("Save changes before continuing?", 18), Text("Your label has unsaved changes."), Row(Choice("Cancel", "cancel"), Choice("Discard", "discard"), Choice("Save", "save")))
        };
        dialog.Closed += (_, _) => result.TrySetResult("cancel");
        await dialog.ShowDialog(this);
        var choice = await result.Task;
        return choice == "discard" || choice == "save" && await SaveAsync(false);
    }
}
