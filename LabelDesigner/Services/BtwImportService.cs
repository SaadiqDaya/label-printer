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
    /// Describes the outcome of a .btw header read.
    /// </summary>
    public enum BtwReadStatus
    {
        /// <summary>Successfully parsed the metadata.</summary>
        Ok,
        /// <summary>File doesn't look like a BarTender file (missing signature).</summary>
        NotBarTender,
        /// <summary>File looks like a BarTender file but the metadata block is missing or malformed.</summary>
        Corrupt,
        /// <summary>Could not read the file (I/O error).</summary>
        ReadError
    }

    public record BtwReadResult(BtwReadStatus Status, BtwMetadata? Metadata, string? ErrorMessage);

    /// <summary>
    /// Reads the .btw header and returns a typed result so the caller can distinguish
    /// "not a BarTender file" from "corrupt BarTender file" from "I/O error".
    /// </summary>
    public static BtwReadResult TryReadHeader(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buf  = new byte[8192];
            int read = fs.Read(buf, 0, buf.Length);
            var header = System.Text.Encoding.UTF8.GetString(buf, 0, read);

            if (!header.Contains("Bar Tender Format File"))
                return new BtwReadResult(BtwReadStatus.NotBarTender, null,
                    "File is missing the BarTender header signature.");

            var match = MetadataRegex.Match(header);
            if (!match.Success)
                return new BtwReadResult(BtwReadStatus.Corrupt, null,
                    "BarTender file has no <Metadata> block.");

            XElement xml;
            try { xml = XElement.Parse(match.Value); }
            catch (Exception ex)
            {
                return new BtwReadResult(BtwReadStatus.Corrupt, null,
                    "Failed to parse <Metadata> XML: " + ex.Message);
            }

            var title   = xml.Element("Title")?.Value
                          ?? Path.GetFileNameWithoutExtension(filePath);
            var printer = xml.Element("Printer")?.Value ?? "";
            var app     = xml.Element("Application")?.Value ?? "";
            var sizeStr = xml.Element("TemplateSize")?.Value ?? "";

            // Parse "50.4 x 25.4 mm". Defaults match the DoorTreats template.
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

            return new BtwReadResult(
                BtwReadStatus.Ok,
                new BtwMetadata(title, width, height, printer, app),
                null);
        }
        catch (Exception ex)
        {
            return new BtwReadResult(BtwReadStatus.ReadError, null, ex.Message);
        }
    }

    /// <summary>
    /// Legacy null-on-failure shim kept for existing call sites. Prefer <see cref="TryReadHeader"/>.
    /// </summary>
    public static BtwMetadata? ReadHeader(string filePath) => TryReadHeader(filePath).Metadata;
}
