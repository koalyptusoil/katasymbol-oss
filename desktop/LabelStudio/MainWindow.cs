using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace LabelStudio;

public sealed partial class MainWindow : Window
{
    LabelDocument document = new();
    readonly LabelCanvas canvas = new();
    readonly StackPanel inspector = new()
    {
        Spacing = 9
    };
    readonly TextBlock status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush("#52634E")
    };
    readonly TextBlock selectionNote = new()
    {
        FontSize = 12,
        Foreground = Brush("#687964")
    };
    readonly TextBlock modelNote = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Foreground = Brush("#687964")
    };
    readonly ComboBox models = new()
    {
        ItemsSource = Catalog.Models,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    readonly ComboBox connections = new()
    {
        ItemsSource = OperatingSystem.IsLinux() ? new[]
        {
            "Automatic",
            "Bluetooth Classic",
            "Bluetooth LE",
            "USB HID",
            "System printer"
        }

        : new[]
        {
            "System printer"
        },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    readonly ComboBox devices = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        PlaceholderText = "Select a discovered printer"
    };
    readonly TextBox target = new()
    {
        Watermark = OperatingSystem.IsLinux() ? "Bluetooth address, /dev/hidraw… or system:queue" : "system:printer name"
    };
    readonly NumericUpDown length = Number(10, 300, 40, .5m);
    readonly NumericUpDown height = Number(4, 300, 12, .5m);
    readonly NumericUpDown density = Number(1, 9, 3, 1);
    readonly NumericUpDown copies = Number(1, 20, 1, 1);
    readonly NumericUpDown channel = Number(1, 30, 1, 1);
    readonly NumericUpDown gap = Number(0, 6, 3, .5m);
    readonly CheckBox dieCut = new()
    {
        Content = "Die-cut labels"
    };
    readonly CheckBox autoLength = new()
    {
        Content = "Auto length · fit text",
        Name = "AutoLength"
    };
    readonly TextBlock lengthNote = Text("", 11, "#74816D");
    Action? refreshSizeFields;
    readonly Button print = new()
    {
        Content = "Print label",
        Padding = new Thickness(24, 12),
        Background = Brush("#2F6844"),
        Foreground = Brushes.White
    };
    readonly List<Button> connectionButtons = [];
    readonly Stack<string> undo = new(), redo = new();
    readonly string[] transports = ["auto", "classic", "ble", "usb", "cups"];
    readonly string settingsPath;
    string? path;
    bool loading, dirty, busy, closingApproved;
    string? lastHistory;
    public MainWindow(string? initialPath = null)
    {
        Title = "Label Studio";
        Width = 1280;
        Height = 920;
        MinWidth = 1020;
        MinHeight = 720;
        Background = Brush("#F5F6F1");
        settingsPath = AppPaths.SettingsFile;
        loading = true;
        RestorePrinterSettings();
        document.HeightMm = Math.Min(document.HeightMm, document.Profile.HeadDots / document.Profile.DotsPerMm);
        foreach (var o in document.Objects)
        {
            o.Y = 1;
            o.Height = document.HeightMm - 2;
        }

        BuildLayout();
        ConnectEditorEvents();
        ConnectWindowEvents();
        loading = false;
        RefreshDocument();
        Render();
        status.Text = "Ready. Double-click text on the label to type directly. Your previous printer settings have been restored.";
        if (initialPath != null)
            try
            {
                Replace(LabelDocument.Load(initialPath), initialPath);
            }
            catch (Exception ex)
            {
                Error(ex);
            }

        Opened += (_, _) =>
        {
            if (Environment.GetEnvironmentVariable("LABEL_STUDIO_SCREENSHOT") is { Length: > 0 } screenshot)
            {
                DispatcherTimer.RunOnce(() =>
                {
                    try
                    {
                        using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                        image.Render(this);
                        image.Save(screenshot);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                    }
                }, TimeSpan.FromSeconds(2));
            }
        };
    }

    void FitObjects()
    {
        foreach (var o in document.Objects)
        {
            o.Width = Math.Min(o.Width, document.LengthMm);
            o.Height = Math.Min(o.Height, document.HeightMm);
            o.X = Math.Clamp(o.X, 0, document.LengthMm - o.Width);
            o.Y = Math.Clamp(o.Y, 0, document.HeightMm - o.Height);
        }
    }

    void RefreshDocument()
    {
        bool limited = document.ApplyAutoLength();
        loading = true;
        models.SelectedItem = document.Profile;
        length.Value = (decimal)document.LengthMm;
        autoLength.IsChecked = document.AutoLength;
        length.IsEnabled = !document.AutoLength;
        lengthNote.IsVisible = document.AutoLength;
        lengthNote.Text = limited ? "Maximum 300 mm reached; text may wrap. Check the preview." : "Grows as you type; keeps text size. Adds 2 mm after text. Use continuous tape.";
        height.Maximum = (decimal)(document.Profile.MaxHeightMm);
        height.Value = (decimal)document.HeightMm;
        density.Maximum = document.Profile.DensityMax;
        density.Value = document.Density;
        dieCut.IsChecked = document.DieCut;
        gap.Value = (decimal)document.GapMm;
        modelNote.Text = document.Profile.Engine == "cups" ? "Requires an installed system printer driver." : $"{document.Profile.DotsPerMm * 25.4:0} dpi · {document.Profile.HeadDots} dot printhead";
        canvas.Document = document;
        loading = false;
        RefreshInspector();
    }

    void Error(Exception ex)
    {
        status.Text = ex.Message;
        status.Foreground = Brush("#9B342A");
    }
}
