using System.Globalization;

namespace LabelDesigner.Services;

/// <summary>
/// Single source of truth for turning the condition-builder UI (field / operator / value) into a
/// clause string that <see cref="ConditionEvaluator"/> can parse. Both the element Properties panel
/// and the Layers tab go through here.
///
/// The numeric operators must emit an UNQUOTED number ({Qty} &gt; 10) — the quoted form the builders
/// historically produced did not match the evaluator's numeric pattern, so those conditions
/// evaluated to "unparseable → never print". (The evaluator now also tolerates the quoted form so
/// templates saved with it keep working.)
/// </summary>
public static class ConditionClauseBuilder
{
    /// <summary>Builds a clause, or returns "" with <paramref name="error"/> set when the input is unusable.</summary>
    public static string Build(string field, string op, string value, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(field)) return "";

        switch (op)
        {
            case "set":
                return $"{{{field}}}";
            case "empty":
                return $"!{{{field}}}";
            case ">" or ">=" or "<" or "<=":
                var t = (value ?? "").Trim();
                if (!double.TryParse(t, NumberStyles.Any, CultureInfo.CurrentCulture, out var n) &&
                    !double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out n))
                {
                    error = $"\"{value}\" is not a number — the {op} comparison needs a numeric value.";
                    return "";
                }
                return $"{{{field}}} {op} {n.ToString(CultureInfo.InvariantCulture)}";
            default:
                return $"{{{field}}} {op} \"{value}\"";
        }
    }
}
