using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelStudio;

public static class SystemPrinting
{
    public const string TargetPrefix = "system:";
    public static bool IsTarget(string address, string transport) => transport == "cups" || address.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase) || address.StartsWith("cups:", StringComparison.OrdinalIgnoreCase);
    // Continue to accept cups: addresses saved by older versions.
    public static string QueueName(string target) => target.StartsWith(TargetPrefix, StringComparison.OrdinalIgnoreCase) ? target[TargetPrefix.Length..] : target.StartsWith("cups:", StringComparison.OrdinalIgnoreCase) ? target[5..] : target;
    public static string[] Queues()
    {
        if (OperatingSystem.IsWindows())
            return WindowsPrinting.Queues();
        return Regex.Matches(ProcessTools.Run("lpstat", "-p"), @"(?m)^printer (\S+)").Select(match => match.Groups[1].Value).ToArray();
    }

    public static string CheckStatus(string target)
    {
        var queue = RequireInstalledQueue(target);
        if (OperatingSystem.IsWindows())
            return WindowsPrinting.CheckStatus(queue);
        return ProcessTools.Run("lpstat", "-p", queue);
    }

    public static string Print(string target, Raster raster, LabelDocument document, int copies)
    {
        if (copies is < 1 or > 20)
            throw new InvalidDataException("Choose between 1 and 20 copies.");
        var queue = RequireInstalledQueue(target);
        if (OperatingSystem.IsWindows())
            return WindowsPrinting.Print(queue, raster, document, copies);
        var path = Path.Combine(Path.GetTempPath(), "label-studio-" + Guid.NewGuid() + ".pdf");
        try
        {
            Rasterizer.Pdf(raster, document.LengthMm, document.HeightMm, path);
            var result = ProcessTools.Run("lp", "-d", queue, "-n", copies.ToString(CultureInfo.InvariantCulture), "-o", FormattableString.Invariant($"media=Custom.{document.HeightMm:g}x{document.LengthMm:g}mm"), "-o", "print-scaling=none", "-o", "fit-to-page=false", path);
            return "Submitted to system printer: " + result;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string RequireInstalledQueue(string target)
    {
        var queue = QueueName(target);
        if (!Queues().Contains(queue, StringComparer.Ordinal))
            throw new InvalidDataException("Choose an installed system printer.");
        return queue;
    }
}
