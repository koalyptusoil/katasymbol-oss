using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LabelStudio;

public static class ProcessTools
{
    public static string Run(string program, params string[] args) => RunTimeout(program, 20000, args);
    public static string RunTimeout(string program, int timeoutMs, params string[] args)
    {
        var info = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.Environment["LC_ALL"] = "C";
        foreach (var arg in args)
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Could not start " + program);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(true);
            throw new TimeoutException(program + " did not respond.");
        }

        var text = output.GetAwaiter().GetResult();
        var err = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new IOException((err + "\n" + text).Trim());
        return Regex.Replace(text, @"\x1b\[[0-9;]*[A-Za-z]", "").Trim();
    }
}
