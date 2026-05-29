namespace LabelDesigner.Services;

/// <summary>
/// Single source of truth for the &amp;&amp;-separated print-condition clause format.
/// ConditionEvaluator and the element/layer view-models both go through here so
/// the split/join behaviour stays consistent.
/// </summary>
public static class PrintConditionParser
{
    private static readonly string[] Separators = { "&&" };

    /// <summary>Returns the individual clauses in <paramref name="condition"/>, trimmed and non-empty.</summary>
    public static IEnumerable<string> SplitClauses(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) yield break;
        foreach (var part in condition.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim();
            if (!string.IsNullOrEmpty(t)) yield return t;
        }
    }

    /// <summary>Joins clauses back into a canonical "a && b && c" string, or null when empty.</summary>
    public static string? Join(IEnumerable<string> clauses)
    {
        var joined = string.Join(" && ", clauses.Where(c => !string.IsNullOrWhiteSpace(c)));
        return string.IsNullOrEmpty(joined) ? null : joined;
    }
}
