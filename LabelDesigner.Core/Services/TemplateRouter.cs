using LabelDesigner.Core.Models;
using System.Globalization;

namespace LabelDesigner.Core.Services;

/// <summary>
/// Resolves which template NAME a batch row should print with. Precedence:
/// 1. the row's explicit "Template" column (any data source can name the template directly),
/// 2. the first matching <see cref="TemplateRoute"/> rule (in list order),
/// 3. the caller's default template (e.g. the watch folder's configured default),
/// 4. null — the row cannot be routed and must be reported, never guessed.
/// </summary>
public static class TemplateRouter
{
    /// <summary>Reserved CSV header that names the template per row.</summary>
    public const string TemplateColumn = "Template";

    public static string? ResolveName(
        IReadOnlyDictionary<string, string> row,
        IReadOnlyList<TemplateRoute> routes,
        string? defaultTemplate)
    {
        if (row.TryGetValue(TemplateColumn, out var explicitName) && !string.IsNullOrWhiteSpace(explicitName))
            return explicitName.Trim();

        foreach (var route in routes)
            if (Matches(route, row))
                return route.TemplateName.Trim();

        return string.IsNullOrWhiteSpace(defaultTemplate) ? null : defaultTemplate.Trim();
    }

    public static bool Matches(TemplateRoute route, IReadOnlyDictionary<string, string> row)
    {
        if (string.IsNullOrWhiteSpace(route.Field) || string.IsNullOrWhiteSpace(route.TemplateName))
            return false;
        if (!row.TryGetValue(route.Field, out var raw)) return false;
        var value = (raw ?? "").Trim();

        switch (route.Operator)
        {
            case RouteOperator.Equals:
                return value.Equals(route.Value.Trim(), StringComparison.OrdinalIgnoreCase);
            case RouteOperator.Contains:
                return route.Value.Trim().Length > 0 &&
                       value.Contains(route.Value.Trim(), StringComparison.OrdinalIgnoreCase);
            case RouteOperator.StartsWith:
                return route.Value.Trim().Length > 0 &&
                       value.StartsWith(route.Value.Trim(), StringComparison.OrdinalIgnoreCase);
            case RouteOperator.EndsWith:
                return route.Value.Trim().Length > 0 &&
                       value.EndsWith(route.Value.Trim(), StringComparison.OrdinalIgnoreCase);
            case RouteOperator.NumericRange:
                // Accept "50", "50.0" and "50ml" style values — parse the leading number.
                if (!TryParseLeadingNumber(value, out var n)) return false;
                return n >= route.Min && n <= route.Max;
            default:
                return false;
        }
    }

    /// <summary>Parses the leading numeric portion of a value ("120ml" → 120). Invariant + current culture.</summary>
    public static bool TryParseLeadingNumber(string value, out double number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        int end = 0;
        while (end < value.Length && (char.IsDigit(value[end]) || value[end] is '.' or ',' or '-' or '+'))
            end++;
        var head = value[..end];
        if (head.Length == 0) return false;

        return double.TryParse(head, NumberStyles.Any, CultureInfo.InvariantCulture, out number)
            || double.TryParse(head, NumberStyles.Any, CultureInfo.CurrentCulture, out number);
    }
}
