using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LabelDesigner.Services;

/// <summary>
/// Minimal dependency-free PDF writer: one page, one full-page image. The label is rendered to a
/// bitmap at template DPI (the same render the printer gets) and embedded FlateDecode-compressed,
/// so the PDF is pixel-identical to the print/preview — render parity, not a re-layout.
/// </summary>
public static class PdfExporter
{
    public static void Write(string path, BitmapSource image, double widthMm, double heightMm)
        => File.WriteAllBytes(path, Build(image, widthMm, heightMm));

    public static byte[] Build(BitmapSource image, double widthMm, double heightMm)
    {
        // Page size in PDF points (1 pt = 1/72 inch).
        double wPt = widthMm * 72.0 / 25.4;
        double hPt = heightMm * 72.0 / 25.4;

        var rgb = ToRgb24(image, out int pxW, out int pxH);
        var compressed = ZlibCompress(rgb);

        var ms = new MemoryStream();
        var offsets = new long[6];   // 1-based object offsets

        void Text(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        string Num(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

        Text("%PDF-1.4\n");

        offsets[1] = ms.Position;
        Text("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = ms.Position;
        Text("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = ms.Position;
        Text($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(wPt)} {Num(hPt)}] " +
             "/Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");

        // Content stream: scale the unit-square image XObject to fill the page.
        var content = Encoding.ASCII.GetBytes($"q\n{Num(wPt)} 0 0 {Num(hPt)} 0 0 cm\n/Im0 Do\nQ\n");
        offsets[4] = ms.Position;
        Text($"4 0 obj\n<< /Length {content.Length} >>\nstream\n");
        ms.Write(content, 0, content.Length);
        Text("\nendstream\nendobj\n");

        offsets[5] = ms.Position;
        Text($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} " +
             $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        ms.Write(compressed, 0, compressed.Length);
        Text("\nendstream\nendobj\n");

        long xrefAt = ms.Position;
        Text("xref\n0 6\n");
        Text("0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++)
            Text(offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Text($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefAt}\n%%EOF\n");

        return ms.ToArray();
    }

    private static byte[] ToRgb24(BitmapSource src, out int width, out int height)
    {
        var fmt = src.Format == PixelFormats.Rgb24 ? src : new FormatConvertedBitmap(src, PixelFormats.Rgb24, null, 0);
        width = fmt.PixelWidth;
        height = fmt.PixelHeight;
        int stride = width * 3;
        var pixels = new byte[height * stride];
        fmt.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    /// <summary>RFC-1950 zlib stream, which is what PDF /FlateDecode expects.</summary>
    private static byte[] ZlibCompress(byte[] data)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }
}
