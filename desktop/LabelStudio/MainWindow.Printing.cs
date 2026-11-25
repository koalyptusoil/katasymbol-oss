using System.Text.Json;
using Avalonia.Threading;

namespace LabelStudio;

public sealed partial class MainWindow
{
    string Transport => !OperatingSystem.IsLinux() ? "cups" : transports[Math.Clamp(connections.SelectedIndex, 0, 4)];

    async Task Work(Func<Task> action)
    {
        if (busy)
            return;
        busy = true;
        status.Foreground = Brush("#52634E");
        UpdatePrint();
        foreach (var b in connectionButtons)
            b.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Error(ex);
        }
        finally
        {
            busy = false;
            foreach (var b in connectionButtons)
                b.IsEnabled = true;
            UpdatePrint();
        }
    }

    void Progress(string value) => Dispatcher.UIThread.Post(() =>
    {
        status.Foreground = Brush("#52634E");
        status.Text = value;
    });
    async Task CheckAsync() => await Work(async () =>
    {
        var doc = document.Clone();
        var address = target.Text?.Trim() ?? "";
        var transport = Transport;
        var ch = (int)(channel.Value ?? 1);
        SaveSettings();
        status.Text = "Connecting to printer…";
        status.Text = await Task.Run(() =>
        {
            if (SystemPrinting.IsTarget(address, transport))
                return SystemPrinting.CheckStatus(address);
            using var wire = Printers.Connect(doc.Profile, transport, address, ch);
            return wire.Status().ToString();
        });
    });
    async Task PrintAsync() => await Work(async () =>
    {
        canvas.EndTextEdit();
        var doc = document.Clone();
        var raster = Rasterizer.Render(doc);
        var address = target.Text?.Trim() ?? "";
        var transport = Transport;
        var ch = (int)(channel.Value ?? 1);
        var n = (int)(copies.Value ?? 1);
        if (address.Length == 0)
            throw new InvalidDataException("Choose a printer first.");
        SaveSettings();
        status.Text = "Preparing label…";
        status.Text = await Task.Run(() =>
        {
            if (SystemPrinting.IsTarget(address, transport))
                return SystemPrinting.Print(address, raster, doc, n);
            using var wire = Printers.Connect(doc.Profile, transport, address, ch);
            Protocol.Print(wire, doc.Profile, raster, doc.Density, n, doc.DieCut, doc.GapMm, Progress);
            return "Label printed. Printer confirmed completion.";
        });
    });
    void RestorePrinterSettings()
    {
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
            var s = settings.RootElement;
            if (s.TryGetProperty("model_id", out var m) && Catalog.Models.Any(p => p.Id == m.GetString()))
                document.ModelId = m.GetString()!;
            if (s.TryGetProperty("target", out var t))
                target.Text = t.GetString();
            if (s.TryGetProperty("transport", out var tr))
                connections.SelectedIndex = OperatingSystem.IsLinux() ? Math.Clamp(tr.GetInt32(), 0, 4) : 0;
            if (s.TryGetProperty("channel", out var ch))
                channel.Value = Math.Clamp(ch.GetInt32(), 1, 30);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
        }
    }

    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(new
            {
                target = target.Text,
                model_id = document.ModelId,
                transport = connections.SelectedIndex,
                channel = (int)(channel.Value ?? 1)
            }, Catalog.Json));
        }
        catch (IOException ex)
        {
            status.Text = "Could not save printer settings: " + ex.Message;
        }
    }
}
