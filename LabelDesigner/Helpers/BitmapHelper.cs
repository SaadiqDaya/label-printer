using LabelDesigner.Core.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace LabelDesigner.Helpers;

public static class BitmapHelper
{
    public static BitmapSource? GenerateBarcode(string value, BarcodeFormatOption format, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var zxingFormat = format switch
            {
                BarcodeFormatOption.QRCode => BarcodeFormat.QR_CODE,
                BarcodeFormatOption.EAN13 => BarcodeFormat.EAN_13,
                BarcodeFormatOption.UPCA => BarcodeFormat.UPC_A,
                BarcodeFormatOption.DataMatrix => BarcodeFormat.DATA_MATRIX,
                BarcodeFormatOption.PDF417 => BarcodeFormat.PDF_417,
                _ => BarcodeFormat.CODE_128
            };

            var writer = new ZXing.Windows.Compatibility.BarcodeWriter
            {
                Format = zxingFormat,
                Options = new EncodingOptions
                {
                    Width = Math.Max(10, width),
                    Height = Math.Max(10, height),
                    Margin = 4
                }
            };

            using var bitmap = writer.Write(value);
            return ToBitmapSource(bitmap);
        }
        catch
        {
            return null;
        }
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.StreamSource = ms;
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    /// <summary>Renders a WPF Visual to a BitmapSource at the given DPI (default 96 = screen, 300 = print).</summary>
    public static RenderTargetBitmap RenderVisual(System.Windows.Media.Visual visual, double width, double height, double dpi = 96)
    {
        var rtb = new RenderTargetBitmap((int)(width * dpi / 96), (int)(height * dpi / 96), dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
