namespace LabelDesigner.Core.Models;

/// <summary>How a <see cref="TemplateRoute"/> compares the row's field value.</summary>
public enum RouteOperator
{
    /// <summary>Field value equals <see cref="TemplateRoute.Value"/> (case-insensitive, trimmed).</summary>
    Equals,
    /// <summary>Field value contains <see cref="TemplateRoute.Value"/> (case-insensitive).</summary>
    Contains,
    /// <summary>Field value begins with <see cref="TemplateRoute.Value"/> (case-insensitive).</summary>
    StartsWith,
    /// <summary>Field value ends with <see cref="TemplateRoute.Value"/> (case-insensitive).</summary>
    EndsWith,
    /// <summary>Field value parses as a number within [<see cref="TemplateRoute.Min"/>, <see cref="TemplateRoute.Max"/>] inclusive.</summary>
    NumericRange,
}

/// <summary>
/// One data→template routing rule: "when this field matches, print the row with that template".
/// Rules are evaluated in list order; the first match wins. A row's explicit "Template" column
/// always beats the rules (see TemplateRouter).
/// </summary>
public class TemplateRoute
{
    /// <summary>The field (CSV header) the rule inspects, e.g. "Volume" or "Size".</summary>
    public string Field { get; set; } = "";

    public RouteOperator Operator { get; set; } = RouteOperator.Equals;

    /// <summary>Comparison value for Equals / Contains.</summary>
    public string Value { get; set; } = "";

    /// <summary>Inclusive numeric bounds for NumericRange.</summary>
    public double Min { get; set; }
    public double Max { get; set; }

    /// <summary>The template NAME (as shown in the template list) the matching row prints with.</summary>
    public string TemplateName { get; set; } = "";
}
