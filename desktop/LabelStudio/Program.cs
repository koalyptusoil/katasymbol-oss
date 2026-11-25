using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;

namespace LabelStudio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
        {
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            return SelfTests.Run();
        }

        if (args.Contains("--printer-status"))
        {
            try
            {
                int i = Array.IndexOf(args, "--printer-status");
                using var wire = Printers.Connect(Catalog.Get(args.Length > i + 2 ? args[i + 2] : "e10"), "classic", args[i + 1], 1);
                Console.WriteLine(wire.Status());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        if (args.Contains("--install"))
        {
            Installer.Install();
            return 0;
        }

        try
        {
            BuildApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "label-studio", "error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.WriteAllText(log, ex.ToString());
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    public static AppBuilder BuildApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(desktop.Args?.FirstOrDefault(a => !a.StartsWith("--")));
        base.OnFrameworkInitializationCompleted();
    }
}
