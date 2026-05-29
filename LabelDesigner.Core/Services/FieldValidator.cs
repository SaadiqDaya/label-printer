using LabelDesigner.Core.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelDesigner.Core.Services;

/// <summary>
/// Pure (no-WPF) per-field validation, run before print so a value that is present-but-wrong (bad
/// length, out of range, not in the allowed set, malformed date/number) is caught before a label
/// is wasted. Returns a human-readable reason, or null when the value is acceptable. The value passed
/// in is the POST-DEFAULT value (defaults are applied upstream).
/// </summary>
public static class FieldValidator
{
    public static string? Validate(FieldDefinition fd, string? value)
    {
        value ??= "";
        bool empty = string.IsNullOrWhiteSpace(value);
        var label = string.IsNullOrWhiteSpace(fd.Prompt) ? fd.Name : fd.Prompt!;

        if (fd.Required && empty) return $"'{label}' is required";
        if (empty) return null; // optional + empty → nothing else to check

        if (fd.MinLength is int min && value.Length < min) return $"'{label}' must be at least {min} characters";
        if (fd.MaxLength is int max && value.Length > max) return $"'{label}' must be at most {max} characters";

        if (fd.DataType == FieldDataType.Number)
        {
            if (!TryNumber(value, out var n)) return $"'{label}' must be a number";
            if (fd.Min is double lo && n < lo) return $"'{label}' must be ≥ {lo}";
            if (fd.Max is double hi && n > hi) return $"'{label}' must be ≤ {hi}";
        }
        else if (fd.DataType == FieldDataType.Date)
        {
            if (!DateTime.TryParse(value, out _)) return $"'{label}' must be a valid date";
        }

        if (fd.AllowedValues is { Count: > 0 } &&
            !fd.AllowedValues.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase)))
            return $"'{label}' must be one of: {string.Join(", ", fd.AllowedValues)}";

        if (!string.IsNullOrWhiteSpace(fd.Pattern))
        {
            try
            {
                if (!Regex.IsMatch(value, "^(?:" + fd.Pattern + ")$"))
                    return $"'{label}' does not match the required format";
            }
            catch { /* a malformed pattern in the template shouldn't block printing */ }
        }
        return null;
    }

    private static bool TryNumber(string v, out double n) =>
        double.TryParse(v, NumberStyles.Any, CultureInfo.CurrentCulture, out n) ||
        double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out n);
}
