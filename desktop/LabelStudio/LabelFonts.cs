using Avalonia.Media;

namespace LabelStudio;

// Documents retain their original family/style strings. Resolve them through
// Avalonia so rendering never depends on Linux command-line tools.
internal static class LabelFonts
{
    public static readonly Lazy<string[]> Installed = new(() =>
        FontManager.Current.SystemFonts
            .SelectMany(family => new[] { family.Name, family.Name + " Bold", family.Name + " Italic", family.Name + " Bold Italic" })
            .Concat(["Sans", "Sans Bold", "Serif", "Monospace"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray());

    // Longest suffixes first; leave words inside family names untouched.
    private static readonly (string Name, FontWeight Weight, FontStyle Style)[] Styles =
    [
        ("Bold Italic", FontWeight.Bold, FontStyle.Italic),
        ("Bold Oblique", FontWeight.Bold, FontStyle.Oblique),
        ("Semi-Bold", FontWeight.SemiBold, FontStyle.Normal),
        ("SemiBold", FontWeight.SemiBold, FontStyle.Normal),
        ("ExtraLight", FontWeight.ExtraLight, FontStyle.Normal),
        ("Bold", FontWeight.Bold, FontStyle.Normal),
        ("Italic", FontWeight.Normal, FontStyle.Italic),
        ("Oblique", FontWeight.Normal, FontStyle.Oblique),
        ("Light", FontWeight.Light, FontStyle.Normal),
        ("Medium", FontWeight.Medium, FontStyle.Normal),
        ("Black", FontWeight.Black, FontStyle.Normal),
        ("Regular", FontWeight.Normal, FontStyle.Normal),
        ("Book", FontWeight.Normal, FontStyle.Normal)
    ];

    public static Typeface Resolve(string description)
    {
        var family = description.Trim();
        var weight = FontWeight.Normal;
        var style = FontStyle.Normal;
        foreach (var suffix in Styles)
        {
            if (!family.EndsWith(" " + suffix.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            family = family[..^(suffix.Name.Length + 1)].TrimEnd();
            weight = suffix.Weight;
            style = suffix.Style;
            break;
        }

        var fontFamily = family.ToLowerInvariant() switch
        {
            "sans" or "sans-serif" => FontFamily.Default,
            "serif" => new FontFamily("Times New Roman, Noto Serif, DejaVu Serif"),
            "monospace" => new FontFamily("Menlo, Consolas, DejaVu Sans Mono"),
            _ => new FontFamily(family)
        };
        return new Typeface(fontFamily, style, weight);
    }
}
