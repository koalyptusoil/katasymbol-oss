using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace LabelStudio;

[SupportedOSPlatform("linux")]
internal static class LinuxDiscovery
{
    public static List<Device> UsbDevices()
    {
        var devices = new List<Device>();
        if (!Directory.Exists("/sys/class/hidraw"))
            return devices;
        var ids = new Dictionary<string, string>
        {
            ["2072"] = "t50m",
            ["2073"] = "t50m_pro",
            ["2074"] = "t50m_plus",
            ["2076"] = "t50s",
            ["2077"] = "t50s",
            ["2075"] = "t80m",
            ["207a"] = "t80m_pro"
        };
        foreach (var entry in Directory.GetDirectories("/sys/class/hidraw"))
        {
            try
            {
                var props = File.ReadAllLines(Path.Combine(entry, "device/uevent")).Where(l => l.Contains('=')).Select(l => l.Split('=', 2)).ToDictionary(v => v[0], v => v[1]);
                var parts = props.GetValueOrDefault("HID_ID", "").Split(':');
                if (parts.Length != 3 || parts[1].ToLowerInvariant() != "00001820")
                    continue;
                var model = ids.TryGetValue(parts[2].TrimStart('0').ToLowerInvariant(), out var id) ? Catalog.Get(id) : null;
                devices.Add(new("/dev/" + Path.GetFileName(entry), model?.Name ?? props.GetValueOrDefault("HID_NAME", "Supvan USB"), model));
            }
            catch (IOException)
            {
            }
        }

        return devices;
    }

    public static List<Device> Discover(Action<string> progress)
    {
        var devices = UsbDevices();
        var errors = new List<string>();
        try
        {
            progress("Looking for Bluetooth printers…");
            ProcessTools.RunTimeout("bluetoothctl", 12000, "--timeout", "7", "scan", "on");
        }
        catch (Exception ex)
        {
            errors.Add("Bluetooth: " + ex.Message);
        }

        try
        {
            foreach (Match m in Regex.Matches(ProcessTools.Run("bluetoothctl", "devices"), @"(?m)^Device ([0-9A-Fa-f:]{17}) (.+)$"))
            {
                var name = m.Groups[2].Value;
                var model = Catalog.Identify(name);
                if (model != null || Regex.IsMatch(name, @"katasymbol|supvan|^[TGD]\d{2}|^LP\d", RegexOptions.IgnoreCase))
                    devices.Add(new(m.Groups[1].Value, name, model));
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        try
        {
            devices.AddRange(SystemPrinting.Queues().Select(q => new Device("cups:" + q, q)));
        }
        catch (Exception ex)
        {
            errors.Add("System printers: " + ex.Message);
        }

        if (devices.Count == 0 && errors.Count > 0)
            throw new IOException(string.Join("\n", errors));
        return devices;
    }

    public static string Pair(string address)
    {
        if (!Printers.IsBluetoothAddress(address))
            throw new InvalidDataException("Enter a Bluetooth printer address first.");
        if (!ProcessTools.Run("bluetoothctl", "info", address).Contains("Paired: yes"))
        {
            var result = ProcessTools.RunTimeout("bluetoothctl", 30000, "--agent", "NoInputNoOutput", "--timeout", "25", "pair", address);
            if (!result.Contains("Pairing successful") && !ProcessTools.Run("bluetoothctl", "info", address).Contains("Paired: yes"))
                throw new IOException(result);
        }

        ProcessTools.Run("bluetoothctl", "trust", address);
        return "Printer paired and trusted.";
    }
}
