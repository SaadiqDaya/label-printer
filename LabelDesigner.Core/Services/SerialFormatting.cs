using System.Globalization;

namespace LabelDesigner.Core.Services;

/// <summary>
/// Formats a serial counter value into its printed string, shared by live resolution and reprint so
/// the two always agree. Decimal (radix 10) uses the .NET format string (e.g. "D4"); base-36 produces
/// alphanumeric 0-9A-Z padded to a width (e.g. "000A"). Prefix/suffix wrap the result ("CTN-0001-A").
/// </summary>
public static class SerialFormatting
{
    public static string Format(long value, string? prefix, string? suffix, int radix, int padWidth, string decimalFormat)
    {
        string core = radix == 36
            ? ToBase36(value).PadLeft(Math.Max(0, padWidth), '0')
            : value.ToString(string.IsNullOrWhiteSpace(decimalFormat) ? "0" : decimalFormat, CultureInfo.CurrentCulture);
        return (prefix ?? "") + core + (suffix ?? "");
    }

    /// <summary>Non-negative base-36 (0-9A-Z). Negative values keep a leading '-'.</summary>
    public static string ToBase36(long value)
    {
        if (value == 0) return "0";
        bool neg = value < 0;
        ulong v = neg ? (ulong)(-value) : (ulong)value;
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var chars = new Stack<char>();
        while (v > 0) { chars.Push(digits[(int)(v % 36)]); v /= 36; }
        return (neg ? "-" : "") + new string(chars.ToArray());
    }
}
