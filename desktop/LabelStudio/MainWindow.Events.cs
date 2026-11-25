using Avalonia.Controls;
using Avalonia.Input;

namespace LabelStudio;

public sealed partial class MainWindow
{
    void ConnectEditorEvents()
    {
        models.SelectionChanged += (_, _) =>
        {
            if (loading || models.SelectedItem is not PrinterProfile selected)
                return;
            PushUndo();
            document.ModelId = selected.Id;
            document.HeightMm = Math.Clamp(document.HeightMm, 4, selected.MaxHeightMm);
            document.Density = Math.Min(document.Density, selected.DensityMax);
            FitObjects();
            RefreshDocument();
            Changed();
        };
        void ResizeLabel(object? s, NumericUpDownValueChangedEventArgs e)
        {
            if (loading)
                return;
            PushUndo();
            document.LengthMm = (double)(length.Value ?? 40);
            document.HeightMm = (double)(height.Value ?? 12);
            FitObjects();
            Changed();
            RefreshInspector();
        }

        length.ValueChanged += ResizeLabel;
        height.ValueChanged += ResizeLabel;
        autoLength.IsCheckedChanged += (_, _) =>
        {
            if (loading)
                return;
            canvas.EndTextEdit();
            PushUndo();
            document.AutoLength = autoLength.IsChecked == true;
            Changed();
            RefreshInspector();
        };
        density.ValueChanged += (_, _) =>
        {
            if (!loading)
            {
                PushUndo();
                document.Density = (int)(density.Value ?? 3);
                Changed();
            }
        };
        gap.ValueChanged += (_, _) =>
        {
            if (!loading)
            {
                PushUndo();
                document.GapMm = (double)(gap.Value ?? 3);
                Changed();
            }
        };
        dieCut.IsCheckedChanged += (_, _) =>
        {
            if (!loading)
            {
                PushUndo();
                document.DieCut = dieCut.IsChecked == true;
                Changed();
            }
        };
        devices.SelectionChanged += (_, _) =>
        {
            if (devices.SelectedItem is not Device d)
                return;
            target.Text = d.Address;
            if (d.Model != null)
                models.SelectedItem = d.Model;
            connections.SelectedIndex = !OperatingSystem.IsLinux() ? 0 : SystemPrinting.IsTarget(d.Address, "auto") ? 4 : d.Address.StartsWith("/dev/") ? 3 : 0;
        };
        target.TextChanged += (_, _) => UpdatePrint();
        connections.SelectionChanged += (_, _) => UpdatePrint();
        print.Click += async (_, _) => await PrintAsync();
    }

    void ConnectWindowEvents()
    {
        KeyDown += async (_, e) =>
        {
            try
            {
                if (EditorShortcuts.HasCommand(e.KeyModifiers))
                {
                    switch (e.Key)
                    {
                        case Key.S:
                            await SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                            break;
                        case Key.O:
                            await OpenAsync();
                            break;
                        case Key.N:
                            await NewAsync();
                            break;
                        case Key.P:
                            await PrintAsync();
                            break;
                        case Key.Z:
                            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                                Redo();
                            else
                                Undo();
                            break;
                        case Key.Y:
                            Redo();
                            break;
                        case Key.D:
                            Duplicate();
                            break;
                        default:
                            return;
                    }

                    e.Handled = true;
                }
                else if (canvas.IsFocused && e.Key is Key.Delete or Key.Back)
                {
                    Delete();
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        };
        Closing += async (_, e) =>
        {
            canvas.EndTextEdit();
            if (closingApproved)
                return;
            if (busy)
            {
                e.Cancel = true;
                status.Text = "Wait for the printer operation to finish before closing.";
                return;
            }

            if (!dirty)
            {
                SaveSettings();
                return;
            }

            e.Cancel = true;
            if (await CanReplace())
            {
                SaveSettings();
                closingApproved = true;
                Close();
            }
        };
    }
}
