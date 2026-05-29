using System.Globalization;
using System.Text.RegularExpressions;

namespace LabelDesigner.Services;

/// <summary>
/// Evaluates a print-condition expression against a field-value dictionary.
/// An empty/null condition always returns true (element always prints).
///
/// Supported syntax (operator STRUCTURE is parsed before {Field} tokens are resolved, so field
/// values containing quotes/operators can't break the parse):
///   {Field} == "value"      — case-insensitive string equality
///   {Field} != "value"      — string inequality
///   {Field} == {Other}      — field-to-field equality (also !=)
///   {Field} &gt;= 10        — numeric comparison (also &gt;, &lt;, &lt;=)
///   {Field}                 — truthy: field is non-empty
///   !{Field}                — falsy: field is empty/whitespace
///   a &amp;&amp; b           — AND
///   a || b                  — OR
///   (a || b) &amp;&amp; c    — parentheses for grouping
///   !( ... )                — NOT of a group
/// Precedence: OR is lowest, then AND, then parentheses/leaf. Backward compatible: a plain
/// &amp;&amp;-joined condition parses exactly as before.
/// </summary>
public static class ConditionEvaluator
{
    public static bool Evaluate(string? condition, Dictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        return EvalExpr(condition.Trim(), fields);
    }

    private static bool EvalExpr(string expr, Dictionary<string, string> fields)
    {
        expr = expr.Trim();
        if (expr.Length == 0) return true;

        // OR has the lowest precedence.
        var orParts = SplitTopLevel(expr, "||");
        if (orParts.Count > 1) return orParts.Any(p => EvalExpr(p, fields));

        // Then AND.
        var andParts = SplitTopLevel(expr, "&&");
        if (andParts.Count > 1) return andParts.All(p => EvalExpr(p, fields));

        // NOT of a parenthesised group.
        if (expr.StartsWith("!(") && IsWrappingParen(expr[1..]))
            return !EvalExpr(expr[2..^1], fields);

        // A whole parenthesised group.
        if (expr.StartsWith("(") && IsWrappingParen(expr))
            return EvalExpr(expr[1..^1], fields);

        return EvaluateSingle(expr, fields);
    }

    /// <summary>Splits on <paramref name="op"/> only where it appears at paren-depth 0 and outside quotes.</summary>
    private static List<string> SplitTopLevel(string expr, string op)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        bool inQuote = false;
        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '"') inQuote = !inQuote;
            else if (!inQuote && c == '(') depth++;
            else if (!inQuote && c == ')') depth--;
            else if (!inQuote && depth == 0 &&
                     i + op.Length <= expr.Length && expr.AsSpan(i, op.Length).SequenceEqual(op))
            {
                parts.Add(expr[start..i]);
                i += op.Length - 1;
                start = i + 1;
            }
        }
        parts.Add(expr[start..]);
        return parts.Count > 1
            ? parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList()
            : parts;
    }

    /// <summary>True when the first '(' matches the final ')', i.e. the whole string is one group.</summary>
    private static bool IsWrappingParen(string expr)
    {
        if (!expr.StartsWith("(") || !expr.EndsWith(")")) return false;
        int depth = 0;
        bool inQuote = false;
        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '"') inQuote = !inQuote;
            else if (!inQuote && c == '(') depth++;
            else if (!inQuote && c == ')')
            {
                depth--;
                if (depth == 0 && i != expr.Length - 1) return false; // closed before the end → not wrapping
            }
        }
        return depth == 0;
    }

    private static bool EvaluateSingle(string condition, Dictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        var trimmed = condition.Trim();

        string Field(string name) => fields.TryGetValue(name, out var v) ? v : "";

        // {Field} == "quoted"
        var eq = Regex.Match(trimmed, @"^\{(\w+)\}\s*==\s*""(.*)""$", RegexOptions.Singleline);
        if (eq.Success)
            return string.Equals(Field(eq.Groups[1].Value), eq.Groups[2].Value, StringComparison.OrdinalIgnoreCase);

        // {Field} != "quoted"
        var neq = Regex.Match(trimmed, @"^\{(\w+)\}\s*!=\s*""(.*)""$", RegexOptions.Singleline);
        if (neq.Success)
            return !string.Equals(Field(neq.Groups[1].Value), neq.Groups[2].Value, StringComparison.OrdinalIgnoreCase);

        // {Field} == {Other} — field-to-field equality
        var eqF = Regex.Match(trimmed, @"^\{(\w+)\}\s*==\s*\{(\w+)\}$");
        if (eqF.Success)
            return string.Equals(Field(eqF.Groups[1].Value), Field(eqF.Groups[2].Value), StringComparison.OrdinalIgnoreCase);

        // {Field} != {Other} — field-to-field inequality
        var neqF = Regex.Match(trimmed, @"^\{(\w+)\}\s*!=\s*\{(\w+)\}$");
        if (neqF.Success)
            return !string.Equals(Field(neqF.Groups[1].Value), Field(neqF.Groups[2].Value), StringComparison.OrdinalIgnoreCase);

        // {Field} OP number — OP in { >=, <=, >, < }
        var num = Regex.Match(trimmed, @"^\{(\w+)\}\s*(>=|<=|>|<)\s*(-?\d+(?:\.\d+)?)$");
        if (num.Success)
        {
            if (double.TryParse(Field(num.Groups[1].Value), NumberStyles.Any, CultureInfo.InvariantCulture, out var lhs) &&
                double.TryParse(num.Groups[3].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var rhs))
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
            return false;
        }

        // !{Field} — empty/falsy check
        var not = Regex.Match(trimmed, @"^!\s*\{(\w+)\}$");
        if (not.Success)
            return string.IsNullOrWhiteSpace(Field(not.Groups[1].Value));

        // Plain {Field} — truthy: non-empty
        var truthy = Regex.Match(trimmed, @"^\{(\w+)\}$");
        if (truthy.Success)
            return !string.IsNullOrWhiteSpace(Field(truthy.Groups[1].Value));

        // Unparseable condition → treat as false (safer than printing the wrong thing).
        return false;
    }
}
