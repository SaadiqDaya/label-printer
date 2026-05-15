using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LabelDesigner.Services;

public record BtwMetadata(
    string Title,
    double WidthMm,
    double HeightMm,
    string Printer,
    string Application
);

public static class BtwImportService
{
    private static readonly Regex MetadataRegex =
        new(@"<Metadata>.*?</Metadata>", RegexOptions.Singleline);

    /// <summary>
    /// Reads only the plain-text header of a .btw file (first 8 KB) and
    /// extracts what it can from the embedded &lt;Metadata&gt; element.
    /// Returns null if the file does not look like a BarTender file.
    /// </summary>
    public static BtwMetadata? ReadHeader(string filePath)
    {
        try
        {
            // The header is always plain ASCII/UTF-8 text before the binary blob starts.
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buf = new byte[8192];
            int read = fs.Read(buf, 0, buf.Length);
            var header = System.Text.Encoding.UTF8.GetString(buf, 0, read);

            // Quick sanity check — real .btw files always contain this string
            if (!header.Contains("Bar Tender Format File"))
                return null;

            var match = MetadataRegex.Match(header);
            if (!match.Success)
                return null;

            var xml = XElement.Parse(match.Value);

            var title   = xml.Element("Title")?.Value
                          ?? Path.GetFileNameWithoutExtension(filePath);
            var printer = xml.Element("Printer")?.Value ?? "";
            var app     = xml.Element("Application")?.Value ?? "";
            var sizeStr = xml.Element("TemplateSize")?.Value ?? "";

            // Parse "50.4 x 25.4 mm"
            double width = 50.8, height = 25.4;
            var sizeParts = sizeStr.Replace("mm", "", StringComparison.OrdinalIgnoreCase)
                                   .Split('x', StringSplitOptions.TrimEntries);
            if (sizeParts.Length == 2)
            {
                double.TryParse(sizeParts[0],
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out width);
                double.TryParse(sizeParts[1],
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out height);
            }

            return new BtwMetadata(title, width, height, printer, app);
        }
        catch
        {
            return null;
        }
    }
}
