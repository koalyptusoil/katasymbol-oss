using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.Versioning;

namespace LabelStudio;

[SupportedOSPlatform("windows")]
internal static class WindowsPrinting
{
    public static string[] Queues() => PrinterSettings.InstalledPrinters.Cast<string>().Order().ToArray();
    public static string CheckStatus(string queue)
    {
        var settings = new PrinterSettings
        {
            PrinterName = queue
        };
        if (!settings.IsValid)
            throw new IOException("The selected Windows printer is unavailable.");
        return "Printer is installed. Check the Windows print queue for device status.";
    }

    public static string Print(string queue, Raster raster, LabelDocument document, int copies)
    {
        using var stream = new MemoryStream(raster.Png());
        using var image = Image.FromStream(stream);
        // The feed direction is the page's long axis, matching the CUPS PDF.
        image.RotateFlip(RotateFlipType.Rotate270FlipNone);
        using var job = new PrintDocument
        {
            DocumentName = "Label Studio label",
            PrintController = new StandardPrintController(),
            PrinterSettings = new PrinterSettings
            {
                PrinterName = queue,
                Copies = (short)copies
            }
        };
        if (!job.PrinterSettings.IsValid)
            throw new IOException("The selected Windows printer is unavailable.");
        var width = (float)(document.HeightMm * 100 / 25.4);
        var height = (float)(document.LengthMm * 100 / 25.4);
        job.DefaultPageSettings.PaperSize = new PaperSize("Label Studio custom label", (int)Math.Round(width), (int)Math.Round(height));
        job.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
        job.DefaultPageSettings.Landscape = false;
        job.PrintPage += (_, page) =>
        {
            var graphics = page.Graphics ?? throw new IOException("The printer did not provide a drawing surface.");
            graphics.PageUnit = GraphicsUnit.Display;
            graphics.TranslateTransform(-page.PageSettings.HardMarginX, -page.PageSettings.HardMarginY);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(image, new RectangleF(0, 0, width, height));
            page.HasMorePages = false;
        };
        job.Print();
        return "Submitted to Windows print queue: " + queue;
    }
}
