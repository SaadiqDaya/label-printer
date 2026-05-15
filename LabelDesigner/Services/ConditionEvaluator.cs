using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelDesigner.Services;

/// <summary>
/// Evaluates a simple print-condition expression against a field-value dictionary.
/// An empty/null condition always returns true (element always prints).
///
/// Supported syntax (after {FieldName} tokens are substituted):
///   {Field} == "value"   — case-insensitive string equality
///   {Field} != "value"   — string inequality
///   {Field} &gt;= 10     — numeric comparison (also &gt;, &lt;, &lt;=)
///   {Field}              — truthy: field is non-empty
///   !{Field}             — falsy: field is empty/whitespace
/// </summary>
public static class ConditionEvaluator
{
    public static bool Evaluate(string? condition, Dictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        // Substitute {FieldName} tokens with their current values
        var expr = Regex.Replace(condition.Trim(), @"\{(\w+)\}", m =>
            fields.TryGetValue(m.Groups[1].Value, out var v) ? v : "");

        // == "quoted string"
        var eq = Regex.Match(expr, @"^(.*?)\s*==\s*""(.*?)""$", RegexOptions.Singleline);
        if (eq.Success)
            return string.Equals(eq.Groups[1].Value.Trim(), eq.Groups[2].Value, StringComparison.OrdinalIgnoreCase);

        // != "quoted string"
        var neq = Regex.Match(expr, @"^(.*?)\s*!=\s*""(.*?)""$", RegexOptions.Singleline);
        if (neq.Success)
            return !string.Equals(neq.Groups[1].Value.Trim(), neq.Groups[2].Value, StringComparison.OrdinalIgnoreCase);

        // Numeric comparisons  >=  <=  >  <
        var num = Regex.Match(expr, @"^(.*?)\s*(>=|<=|>|<)\s*(-?\d+(?:\.\d+)?)$");
        if (num.Success &&
            double.TryParse(num.Groups[1].Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lhs) &&
            double.TryParse(num.Groups[3].Value,          NumberStyles.Any, CultureInfo.InvariantCulture, out var rhs))
        {
            return num.Groups[2].Value switch
            {
                ">=" => lhs >= rhs,
                "<=" => lhs <= rhs,
                ">"  => lhs > rhs,
                "<"  => lhs < rhs,
                _    => true
            };
        }

        // !{Field} — empty/falsy check
        var not = Regex.Match(expr, @"^!\s*(.*)$", RegexOptions.Singleline);
        if (not.Success)
            return string.IsNullOrWhiteSpace(not.Groups[1].Value);

        // Plain {Field} — truthy: non-empty
        return !string.IsNullOrWhiteSpace(expr);
    }
}
