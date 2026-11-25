using System.Text.Json;
using System.Text.Json.Serialization;

namespace LabelStudio;

public sealed class LabelObject
{
    public string Name { get; set; } = "";
    public bool Visible { get; set; } = true;
    public bool Locked
    {
        get; set;
    }
    public string Kind { get; set; } = "text";
    public double X { get; set; } = 2;
    public double Y { get; set; } = 2;
    public double Width { get; set; } = 36;
    public double Height { get; set; } = 8;
    public string Text { get; set; } = "Made on Linux";
    public string Font { get; set; } = "Sans Bold";
    public double FontMm { get; set; } = 4;
    public string Align { get; set; } = "center";
    public string Image { get; set; } = "";
    public double StrokeMm { get; set; } = 0.25;

    public LabelObject Clone() => (LabelObject)MemberwiseClone();
}

public sealed class LabelDocument
{
    public string Format { get; set; } = "label-studio";
    public int Version { get; set; } = 2;
    public string ModelId { get; set; } = "e10";
    public double LengthMm { get; set; } = 40;
    public bool AutoLength
    {
        get; set;
    }
    public double HeightMm { get; set; } = 12;
    public int Density { get; set; } = 3;
    public bool DieCut
    {
        get; set;
    }
    public double GapMm { get; set; } = 3;
    public List<LabelObject> Objects { get; set; } = [new()];

    [JsonIgnore]
    public PrinterProfile Profile => Catalog.Get(ModelId);

    // Returns true when the longest line exceeds the supported 300 mm length.
    public bool ApplyAutoLength()
    {
        if (!AutoLength)
            return false;
        var oldLength = LengthMm;
        var widths = new Dictionary<LabelObject, double>();
        var frames = new Dictionary<LabelObject, double>();
        double required = 10;
        foreach (var o in Objects)
        {
            if (o.Visible && !o.Locked && o.Kind == "text")
            {
                using var geometry = new TextGeometry(o, Profile.DotsPerMm);
                var width = Math.Max(.25, geometry.NaturalWidthMm);
                widths.Add(o, width);
                required = Math.Max(required, o.X + width + 2);
            }
            else if (o.Visible && !o.Locked && o.Kind == "border" && o.Width >= oldLength * .75 && oldLength - o.X - o.Width <= 2)
            {
                // Page frames retain their left/right insets as the tape changes length.
                frames.Add(o, Math.Max(0, oldLength - o.X - o.Width));
                required = Math.Max(required, o.X + frames[o] + .25);
            }
            else
                required = Math.Max(required, o.X + o.Width);
        }

        var dotsPerMm = Profile.DotsPerMm;
        LengthMm = Math.Clamp(Math.Ceiling(required * dotsPerMm) / dotsPerMm, 10, 300);
        foreach (var (o, width) in widths)
            o.Width = Math.Min(width, Math.Min(LengthMm - o.X, Math.Max(.25, LengthMm - o.X - 2)));
        foreach (var (o, inset) in frames)
            o.Width = LengthMm - o.X - inset;
        return required > 300;
    }

    public void Validate()
    {
        if (Format != "label-studio" || Version != 2)
            throw new InvalidDataException("Unsupported label format.");
        if (!InRange(LengthMm, 10, 300) || !InRange(HeightMm, 4, Profile.MaxHeightMm))
            throw new InvalidDataException("Label dimensions do not fit this printer's printhead.");
        if (Density < 1 || Density > Profile.DensityMax || !InRange(GapMm, 0, 6))
            throw new InvalidDataException("Invalid density or label gap.");
        if (Objects is null || Objects.Count > 200)
            throw new InvalidDataException("Too many label objects.");
        foreach (var item in Objects)
            ValidateObject(item);
    }

    private static bool InRange(double value, double minimum, double maximum) => double.IsFinite(value) && value >= minimum && value <= maximum;
    private void ValidateObject(LabelObject item)
    {
        if (item is null || item.Kind is not ("text" or "image" or "border"))
            throw new InvalidDataException("Invalid label object type.");
        var fits = InRange(item.X, 0, LengthMm) && InRange(item.Y, 0, HeightMm) && InRange(item.Width, .1, LengthMm) && InRange(item.Height, .1, HeightMm) && item.X + item.Width <= LengthMm + .001 && item.Y + item.Height <= HeightMm + .001;
        if (!fits)
            throw new InvalidDataException("An object extends outside the label. Move or resize it to fit.");
        if (item.Name is null || item.Name.Length > 128 || item.Name.Contains('\0'))
            throw new InvalidDataException("Invalid layer name.");
        if (item.Text is null || item.Text.Length > 10000 || item.Font is null || item.Font.Length > 512 || item.Font.Contains('\0') || !InRange(item.FontMm, .25, 100))
            throw new InvalidDataException("Invalid object text or font.");
        if (item.Align is not ("left" or "center" or "right"))
            throw new InvalidDataException("Invalid text alignment.");
        if (item.Image is null || item.Image.Length > 12000000 || !InRange(item.StrokeMm, .05, 5))
            throw new InvalidDataException("Invalid artwork or border width.");
    }

    public string Serialize()
    {
        Validate();
        ApplyAutoLength();
        Validate();
        return JsonSerializer.Serialize(this, Catalog.Json);
    }

    public LabelDocument Clone() => JsonSerializer.Deserialize<LabelDocument>(Serialize(), Catalog.Json)!;
    public void Save(string path)
    {
        var text = Serialize();
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    public static LabelDocument Load(string path)
    {
        if (new FileInfo(path).Length > 25000000)
            throw new InvalidDataException("Label file is too large.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        LabelDocument doc;
        if (root.GetProperty("format").GetString() == "e10-label" && root.GetProperty("version").GetInt32() == 1)
            doc = ImportLegacy(root.GetProperty("label"));
        else
            doc = root.Deserialize<LabelDocument>(Catalog.Json) ?? throw new InvalidDataException("Invalid label document.");
        doc.Validate();
        doc.ApplyAutoLength();
        return doc;
    }

    private static LabelDocument ImportLegacy(JsonElement j)
    {
        string S(string n, string d = "") => j.TryGetProperty(n, out var v) ? v.GetString() ?? d : d;
        double N(string n, double d) => j.TryGetProperty(n, out var v) ? v.GetDouble() : d;
        bool B(string n, bool d = false) => j.TryGetProperty(n, out var v) ? v.GetBoolean() : d;
        var doc = new LabelDocument
        {
            ModelId = S("model_id", "e10"),
            LengthMm = N("length_mm", 40),
            HeightMm = N("height_mm", 12),
            Density = (int)N("density", 3),
            DieCut = B("die_cut"),
            GapMm = N("gap_mm", 3),
            Objects = []
        };
        // Flatten the original layout once so legacy designs remain a single artwork object.
        doc.Validate();
        var raster = Rasterizer.Legacy(j, doc);
        doc.Objects.Add(new()
        {
            Kind = "image",
            X = 0,
            Y = 0,
            Width = doc.LengthMm,
            Height = doc.HeightMm,
            Image = Convert.ToBase64String(raster.Png())
        });
        return doc;
    }
}
