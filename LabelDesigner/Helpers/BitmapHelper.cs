using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.ViewModels;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace LabelDesigner.Helpers;

public static class BitmapHelper
{
    /// <summary>
    /// Generates a barcode bitmap using ZXing's raw pixel-data writer so no
    /// human-readable text is ever added by the library itself.
    /// When <paramref name="showText"/> is true we draw the text ourselves below the bars.
    /// Always renders at a high internal resolution for crisp display and print quality.
    /// </summary>
    public static BitmapSource? GenerateBarcode(
        string value,
        BarcodeFormatOption format,
        int width,
        int height,
        bool showText = false,
        string fontFamily = "Arial",
        float fontSize = 8f,
        int qualityMultiplier = 4,
        int errorCorrection = 1)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            bool is2D     = BarcodeFormatOptions.Is2D(format);
            bool drawText = showText && !is2D;   // 2-D formats carry no human-readable line

            int qm      = Math.Max(1, qualityMultiplier);
            int renderW = Math.Max(1, width  * qm);
            int renderH = Math.Max(1, height * qm);

            // Reserve vertical space proportional to the requested font size.
            int textH, barH;
            if (drawText)
            {
                float scaledFont = Math.Max(6f, fontSize) * qm;
                textH = (int)(scaledFont * 1.8f);          // line height with padding
                textH = Math.Min(textH, renderH / 2);      // cap at half of total height
                textH = Math.Max(textH, 16);               // at least 16 px for legibility
                barH  = renderH - textH;
            }
            else
            {
                textH = 0;
                barH  = renderH;
            }

            // Quiet zone: ISO/IEC 15417 (Code128) etc. want ~10× the module width on 1-D codes.
            // ZXing's Margin is measured in MODULES, so 10 (1-D) / 4 (2-D) is a real clear zone,
            // unlike the old hardcoded 2.
            int margin = is2D ? 4 : 10;

            // GS1-128: drop the (AI) parentheses and let ZXing insert the FNC1 (GS1 mode);
            // 1-D retail codes get their check digit auto-completed when the user omitted it.
            string payload = format == BarcodeFormatOption.GS1_128
                ? value.Replace("(", "").Replace(")", "")
                : BarcodeValidator.NormalizeForEncoding(format, value);

            // ── BarcodeWriterPixelData produces raw BGRA pixels, no text ever ──
            var pixelWriter = new BarcodeWriterPixelData
            {
                Format  = MapFormat(format),
                Options = BuildOptions(format, renderW, barH, margin, errorCorrection)
            };

            var pixelData = pixelWriter.Write(payload);

            // Hold the GDI bitmaps in `using` so they're disposed on every exit path.
            using var barsBmp  = PixelDataToBitmap(pixelData);
            using var finalBmp = new Bitmap(renderW, renderH, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(finalBmp))
            {
                g.Clear(Color.White);
                g.DrawImage(barsBmp, 0, 0, renderW, barH);

                if (drawText)
                {
                    float renderFontSize = Math.Max(6f, Math.Min(fontSize * qm, textH * 0.85f));
                    DrawCentredText(g, value, fontFamily, renderFontSize,
                                    new RectangleF(0, barH, renderW, textH));
                }
            }

            return ToBitmapSource(finalBmp);
        }
        catch (Exception ex)
        {
            // No longer silent: a failed encode is logged. The caller (PrintService) validates first
            // and renders a visible error placeholder, so this never prints a silent blank.
            Services.LogService.Error($"Barcode generation failed for {format} '{value}'.", ex);
            return null;
        }
    }

    private static BarcodeFormat MapFormat(BarcodeFormatOption f) => f switch
    {
        BarcodeFormatOption.QRCode     => BarcodeFormat.QR_CODE,
        BarcodeFormatOption.EAN13      => BarcodeFormat.EAN_13,
        BarcodeFormatOption.UPCA       => BarcodeFormat.UPC_A,
        BarcodeFormatOption.DataMatrix => BarcodeFormat.DATA_MATRIX,
        BarcodeFormatOption.PDF417     => BarcodeFormat.PDF_417,
        BarcodeFormatOption.Code39     => BarcodeFormat.CODE_39,
        BarcodeFormatOption.Code93     => BarcodeFormat.CODE_93,
        BarcodeFormatOption.ITF        => BarcodeFormat.ITF,
        BarcodeFormatOption.Codabar    => BarcodeFormat.CODABAR,
        BarcodeFormatOption.Aztec      => BarcodeFormat.AZTEC,
        _                              => BarcodeFormat.CODE_128   // Code128 and GS1_128
    };

    private static EncodingOptions BuildOptions(BarcodeFormatOption format, int width, int height, int margin, int ecc)
    {
        switch (format)
        {
            case BarcodeFormatOption.GS1_128:
                return new ZXing.OneD.Code128EncodingOptions
                    { Width = width, Height = height, Margin = margin, GS1Format = true };
            case BarcodeFormatOption.QRCode:
                return new ZXing.QrCode.QrCodeEncodingOptions
                    { Width = width, Height = height, Margin = margin, ErrorCorrection = ToQrEcc(ecc) };
            default:
                return new EncodingOptions { Width = width, Height = height, Margin = margin };
        }
    }

    private static ZXing.QrCode.Internal.ErrorCorrectionLevel ToQrEcc(int level) => level switch
    {
        0 => ZXing.QrCode.Internal.ErrorCorrectionLevel.L,
        2 => ZXing.QrCode.Internal.ErrorCorrectionLevel.Q,
        3 => ZXing.QrCode.Internal.ErrorCorrectionLevel.H,
        _ => ZXing.QrCode.Internal.ErrorCorrectionLevel.M
    };

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Converts ZXing PixelData (BGRA byte array) to a System.Drawing.Bitmap.</summary>
    private static Bitmap PixelDataToBitmap(PixelData pixelData)
    {
        var bmp     = new Bitmap(pixelData.Width, pixelData.Height, PixelFormat.Format32bppArgb);
        var bmpData = bmp.LockBits(
            new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        Marshal.Copy(pixelData.Pixels, 0, bmpData.Scan0, pixelData.Pixels.Length);
        bmp.UnlockBits(bmpData);
        return bmp;
    }

    private static void DrawCentredText(Graphics g, string text, string fontFamily, float px, RectangleF rect)
    {
        var sf = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        try
        {
            using var font  = new Font(fontFamily, px, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.Black);
            g.DrawString(text, font, brush, rect, sf);
        }
        catch
        {
            using var font  = new Font(FontFamily.GenericSansSerif, px, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.Black);
            g.DrawString(text, font, brush, rect, sf);
        }
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        // BitmapCacheOption.OnLoad copies the stream contents at EndInit() so disposing the
        // MemoryStream afterwards is safe. The using/try-finally guarantees we don't leak the
        // stream if BeginInit/EndInit/Freeze throws (e.g. on malformed PNG buffer).
        var ms = new MemoryStream();
        try
        {
            bitmap.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = ms;
            bi.CacheOption  = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        finally
        {
            ms.Dispose();
        }
    }

    /// <summary>
    /// Conditions an image for thermal print: grayscale / hard threshold / Floyd-Steinberg dither,
    /// with optional inversion. Color mode is a pass-through (unless inverted). Doing this in-app at
    /// the printer's dot grid gives WYSIWYG and far better mono-thermal logos than the driver's own
    /// halftoning. Returns a frozen BitmapSource; falls back to the source on any error.
    /// </summary>
    public static System.Windows.Media.Imaging.BitmapSource ConditionImage(
        System.Windows.Media.Imaging.BitmapSource src, ImageRenderMode mode, bool invert, int threshold)
    {
        if (mode == ImageRenderMode.Color && !invert) return src;
        try
        {
            var fmt = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int w = fmt.PixelWidth, h = fmt.PixelHeight, stride = w * 4;
            var px = new byte[h * stride];
            fmt.CopyPixels(px, stride, 0);

            if (mode == ImageRenderMode.Color) // invert only, keep colour
            {
                for (int o = 0; o < px.Length; o += 4)
                { px[o] = (byte)(255 - px[o]); px[o + 1] = (byte)(255 - px[o + 1]); px[o + 2] = (byte)(255 - px[o + 2]); }
            }
            else
            {
                var gray = new double[w * h];
                for (int i = 0; i < w * h; i++)
                {
                    int o = i * 4;
                    double y = 0.299 * px[o + 2] + 0.587 * px[o + 1] + 0.114 * px[o];
                    gray[i] = invert ? 255 - y : y;
                }

                if (mode == ImageRenderMode.Dither)
                {
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            int idx = y * w + x;
                            double oldV = gray[idx];
                            double newV = oldV < 128 ? 0 : 255;
                            double err = oldV - newV;
                            gray[idx] = newV;
                            if (x + 1 < w) gray[idx + 1] += err * 7 / 16;
                            if (y + 1 < h)
                            {
                                if (x > 0) gray[idx + w - 1] += err * 3 / 16;
                                gray[idx + w] += err * 5 / 16;
                                if (x + 1 < w) gray[idx + w + 1] += err * 1 / 16;
                            }
                        }
                }
                else if (mode == ImageRenderMode.Threshold)
                {
                    for (int i = 0; i < w * h; i++) gray[i] = gray[i] >= threshold ? 255 : 0;
                }

                for (int i = 0; i < w * h; i++)
                {
                    int o = i * 4;
                    byte v = (byte)Math.Clamp(gray[i], 0, 255);
                    px[o] = px[o + 1] = px[o + 2] = v; px[o + 3] = 255;
                }
            }

            var outBmp = System.Windows.Media.Imaging.BitmapSource.Create(
                w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, stride);
            outBmp.Freeze();
            return outBmp;
        }
        catch (Exception ex)
        {
            Services.LogService.Error("Image conditioning failed; using original.", ex);
            return src;
        }
    }

    /// <summary>Packs a bitmap into 1-bpp rows (MSB-first, black bit = printed dot) for a ZPL ^GFA field.
    /// Dark, opaque pixels become black; light/transparent pixels become white.</summary>
    public static Services.ZplBitmap ToZplMono(System.Windows.Media.Imaging.BitmapSource src)
    {
        var fmt = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int w = fmt.PixelWidth, h = fmt.PixelHeight, stride = w * 4;
        var px = new byte[h * stride];
        fmt.CopyPixels(px, stride, 0);

        int bytesPerRow = (w + 7) / 8;
        var data = new byte[bytesPerRow * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = y * stride + x * 4;
                double alpha = px[o + 3] / 255.0;
                double lum = 0.299 * px[o + 2] + 0.587 * px[o + 1] + 0.114 * px[o];
                if (alpha > 0.5 && lum < 128)
                    data[y * bytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        return new Services.ZplBitmap(data, w, h, bytesPerRow);
    }

    /// <summary>Renders a WPF Visual to a BitmapSource at the given DPI.</summary>
    public static System.Windows.Media.Imaging.RenderTargetBitmap RenderVisual(
        System.Windows.Media.Visual visual, double width, double height, double dpi = 96)
    {
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(width * dpi / 96), (int)(height * dpi / 96),
            dpi, dpi,
            System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
