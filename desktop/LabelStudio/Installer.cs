using System.Diagnostics;

namespace LabelStudio;

public static class Installer
{
    public static void Install()
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("This installer is for Linux.");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(home, ".local/share");
        var source = Environment.ProcessPath ?? throw new IOException("Cannot locate the executable.");
        var appDir = Path.Combine(data, "label-studio");
        Directory.CreateDirectory(appDir);
        var destination = Path.Combine(appDir, "LabelStudio");
        if (Path.GetFullPath(source) != Path.GetFullPath(destination))
        {
            var tmp = destination + ".new";
            File.Copy(source, tmp, true);
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            File.Move(tmp, destination, true);
        }

        var licenseSource = Path.Combine(Path.GetDirectoryName(source)!, "Licenses");
        var licenseDestination = Path.Combine(appDir, "Licenses");
        if (Directory.Exists(licenseSource) && Path.GetFullPath(licenseSource) != Path.GetFullPath(licenseDestination))
        {
            Directory.CreateDirectory(licenseDestination);
            foreach (var license in Directory.GetFiles(licenseSource))
                File.Copy(license, Path.Combine(licenseDestination, Path.GetFileName(license)), true);
        }

        var applications = Path.Combine(data, "applications");
        Directory.CreateDirectory(applications);
        var mimeDir = Path.Combine(data, "mime/packages");
        Directory.CreateDirectory(mimeDir);
        File.WriteAllText(Path.Combine(mimeDir, "label-studio.xml"), """
        <?xml version="1.0" encoding="UTF-8"?>
        <mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info"><mime-type type="application/x-label-studio"><comment>Label Studio design</comment><glob pattern="*.labelstudio"/><glob pattern="*.e10label"/></mime-type></mime-info>
        """);
        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        File.WriteAllText(Path.Combine(applications, "io.github.labelstudio.Labels.desktop"), $"""
        [Desktop Entry]
        Type=Application
        Name=Label Studio
        Comment=Design and print Katasymbol and Supvan labels
        Exec="{Escape(destination)}" %f
        TryExec={destination}
        Icon=printer
        Terminal=false
        Categories=Graphics;
        MimeType=application/x-label-studio;
        StartupNotify=true
        StartupWMClass=LabelStudio
        """ + Environment.NewLine);
        foreach (var (program, argument) in new[]
        {
            ("update-desktop-database", applications),
            ("update-mime-database", Path.Combine(data, "mime"))
        }

        )
            try
            {
                ProcessTools.Run(program, argument);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }

        Console.WriteLine("Installed Label Studio: " + destination);
    }
}
