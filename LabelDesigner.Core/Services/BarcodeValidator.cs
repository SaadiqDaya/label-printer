using LabelDesigner.Core.Models;

namespace LabelDesigner.Core.Services;

/// <summary>
/// Pure (no-WPF, no-ZXing) symbology validation and check-digit math, so a bad barcode is caught
/// at design time / before print rather than silently printing blank. Lives in Core so it is unit
/// testable. Returns a human-readable reason, or null when the value is encodable.
/// </summary>
public static class BarcodeValidator
{
    private const string Code39Charset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    /// <summary>Null when OK, otherwise a short reason naming what is wrong.</summary>
    public static string? Validate(BarcodeFormatOption format, string? value)
    {
        if (string.IsNullOrEmpty(value)) return "value is empty";

        switch (format)
        {
            case BarcodeFormatOption.EAN13:
                if (!AllDigits(value)) return "EAN-13 accepts digits only";
                if (value.Length is not (12 or 13)) return "EAN-13 needs 12 digits (check digit added) or 13";
                if (value.Length == 13 && !CheckDigitValid(value)) return "EAN-13 check digit is wrong";
                return null;

            case BarcodeFormatOption.UPCA:
                if (!AllDigits(value)) return "UPC-A accepts digits only";
                if (value.Length is not (11 or 12)) return "UPC-A needs 11 digits (check digit added) or 12";
                if (value.Length == 12 && !CheckDigitValid("0" + value)) return "UPC-A check digit is wrong";
                return null;

            case BarcodeFormatOption.ITF:
                if (!AllDigits(value)) return "ITF (Interleaved 2 of 5) accepts digits only";
                if (value.Length % 2 != 0) return "ITF needs an even number of digits";
                return null;

            case BarcodeFormatOption.Code39:
                foreach (var c in value.ToUpperInvariant())
                    if (!Code39Charset.Contains(c)) return $"Code 39 cannot encode '{c}'";
                return null;

            case BarcodeFormatOption.Codabar:
                foreach (var c in value)
                    if (!"0123456789-$:/.+ABCD".Contains(char.ToUpperInvariant(c)))
                        return $"Codabar cannot encode '{c}'";
                return null;

            case BarcodeFormatOption.GS1_128:
                // Application-identifier form "(01)..." — sanity check the (AI) parentheses are balanced;
                // the full AI table is enforced at encode time.
                if (value.Count(c => c == '(') != value.Count(c => c == ')'))
                    return "GS1-128 application identifiers must be balanced parentheses, e.g. (01)09501101...";
                return null;

            case BarcodeFormatOption.Code128:
            case BarcodeFormatOption.Code93:
                foreach (var c in value)
                    if (c > 127) return $"{format} encodes ASCII only; '{c}' is not supported";
                return null;

            // 2-D symbologies: any non-empty payload is fine.
            case BarcodeFormatOption.QRCode:
            case BarcodeFormatOption.DataMatrix:
            case BarcodeFormatOption.PDF417:
            case BarcodeFormatOption.Aztec:
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns the value with a check digit appended where the symbology expects one and the user
    /// left it off (EAN-13 from 12 digits, UPC-A from 11). Otherwise returns the value unchanged.
    /// </summary>
    public static string NormalizeForEncoding(BarcodeFormatOption format, string value)
    {
        if (string.IsNullOrEmpty(value) || !AllDigits(value)) return value;
        return format switch
        {
            BarcodeFormatOption.EAN13 when value.Length == 12 => value + Ean13CheckDigit(value),
            BarcodeFormatOption.UPCA  when value.Length == 11 => value + Ean13CheckDigit("0" + value),
            _ => value
        };
    }

    public static bool AllDigits(string s)
    {
        foreach (var c in s) if (c < '0' || c > '9') return false;
        return true;
    }

    /// <summary>Standard GS1 mod-10 check digit for a 12-digit EAN-13 body (or zero-padded UPC-A).</summary>
    public static char Ean13CheckDigit(string twelveDigits)
    {
        int sum = 0;
        for (int i = 0; i < twelveDigits.Length; i++)
        {
            int d = twelveDigits[i] - '0';
            sum += (i % 2 == 0) ? d : d * 3;
        }
        int check = (10 - (sum % 10)) % 10;
        return (char)('0' + check);
    }

    private static bool CheckDigitValid(string thirteenDigits) =>
        Ean13CheckDigit(thirteenDigits[..12]) == thirteenDigits[12];
}
