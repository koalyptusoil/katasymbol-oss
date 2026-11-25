namespace LabelStudio;

internal static class AppPaths
{
    public static string SettingsFile => Path.Combine(ConfigurationDirectory, "e10-studio", "settings.json");

    private static string ConfigurationDirectory
    {
        get
        {
            if (!OperatingSystem.IsLinux())
                return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var configured = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured) ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
    }
}
