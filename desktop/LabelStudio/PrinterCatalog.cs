using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelStudio;

public sealed class PrinterProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Engine { get; set; } = "";
    public double DotsPerMm
    {
        get; set;
    }
    public int HeadDots
    {
        get; set;
    }
    public int DensityMax
    {
        get; set;
    }
    public string Transport { get; set; } = "classic";
    public string[] Prefixes { get; set; } = [];

    [JsonIgnore]
    public bool RequiresSystemDriver => Engine == "cups";

    [JsonIgnore]
    public double MaxHeightMm => RequiresSystemDriver ? 300 : HeadDots / DotsPerMm;

    public override string ToString() => Name + (RequiresSystemDriver ? " · system driver" : "");
}

public static class Catalog
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };
    public static readonly IReadOnlyList<PrinterProfile> Models = Load().AsReadOnly();
    private static List<PrinterProfile> Load()
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("LabelStudio.printers.json")!;
        using var doc = JsonDocument.Parse(source);
        return doc.RootElement.GetProperty("models").Deserialize<List<PrinterProfile>>(Json)!;
    }

    public static PrinterProfile Get(string id) => Models.FirstOrDefault(m => m.Id == id) ?? throw new InvalidDataException("Unknown printer model.");
    public static PrinterProfile? Identify(string name)
    {
        var matches = Models.SelectMany(m => m.Prefixes.Where(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)).Select(p => (p.Length, Model: m))).ToList();
        if (matches.Count > 0)
        {
            var longestPrefix = matches.Max(match => match.Length);
            var best = matches.Where(match => match.Length == longestPrefix).Select(match => match.Model).ToList();
            return best.Select(m => (m.Engine, m.HeadDots, m.DotsPerMm, m.Transport)).Distinct().Count() == 1 ? best[0] : null;
        }

        string Clean(string s) => new(s.ToLowerInvariant().Replace("katasymbol", "").Replace("supvan", "").Where(char.IsLetterOrDigit).ToArray());
        return Models.SingleOrDefault(m => Clean(m.Name) == Clean(name));
    }
}
