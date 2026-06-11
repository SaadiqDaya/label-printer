using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using Xunit;

namespace LabelDesigner.Tests;

/// <summary>Regression tests for the quoted-numeric print-condition defect: the condition builders
/// emitted {Field} &gt; "10" (quoted), which the evaluator rejected as unparseable → the layer or
/// element NEVER printed. The evaluator now accepts both forms and the builder emits the clean one.</summary>
public class NumericConditionRegressionTests
{
    private static Dictionary<string, string> F(string qty) =>
        new(StringComparer.OrdinalIgnoreCase) { ["Qty"] = qty };

    [Fact]
    public void Evaluator_AcceptsUnquotedAndQuotedNumbers()
    {
        Assert.True(ConditionEvaluator.Evaluate("{Qty} > 10", F("11")));
        Assert.False(ConditionEvaluator.Evaluate("{Qty} > 10", F("9")));
        // The legacy quoted form (already saved inside existing .lbl templates) must keep working.
        Assert.True(ConditionEvaluator.Evaluate("{Qty} > \"10\"", F("11")));
        Assert.False(ConditionEvaluator.Evaluate("{Qty} >= \"10\"", F("9")));
        Assert.True(ConditionEvaluator.Evaluate("{Qty} <= \"10\"", F("10")));
    }

    [Fact]
    public void Builder_EmitsUnquotedNumbers_AndRefusesJunk()
    {
        Assert.Equal("{Qty} > 10", ConditionClauseBuilder.Build("Qty", ">", "10", out var e1));
        Assert.Null(e1);
        Assert.Equal("{Qty} >= 2.5", ConditionClauseBuilder.Build("Qty", ">=", " 2.5 ", out var e2));
        Assert.Null(e2);

        Assert.Equal("", ConditionClauseBuilder.Build("Qty", ">", "lots", out var err));
        Assert.NotNull(err);   // fail loud, not a silently never-true clause
    }

    [Fact]
    public void Builder_OtherOperators()
    {
        Assert.Equal("{Color} == \"Red\"", ConditionClauseBuilder.Build("Color", "==", "Red", out _));
        Assert.Equal("{Color}", ConditionClauseBuilder.Build("Color", "set", "", out _));
        Assert.Equal("!{Color}", ConditionClauseBuilder.Build("Color", "empty", "", out _));
        Assert.Equal("", ConditionClauseBuilder.Build("", "==", "x", out _));   // no field → no clause
    }

    [Fact]
    public void BuiltClause_RoundTripsThroughEvaluator()
    {
        var clause = ConditionClauseBuilder.Build("Qty", ">", "10", out _);
        Assert.True(ConditionEvaluator.Evaluate(clause, F("12")));
        Assert.False(ConditionEvaluator.Evaluate(clause, F("8")));
    }
}

/// <summary>Layer print conditions must gate every element on the layer, end to end (ZPL output path).</summary>
public class LayerConditionEndToEndTests
{
    private static LabelTemplate Build(string? layerCondition)
    {
        var t = new LabelTemplate { Dpi = 203 };
        var layer = new Layer { Name = "Promo", PrintCondition = layerCondition };
        t.Layers.Add(layer);
        t.Elements.Add(new TextElement { Text = "ALWAYS", Width = 80, Height = 20 });
        t.Elements.Add(new TextElement { Text = "PROMO-ONLY", Width = 80, Height = 20, Y = 30, LayerId = layer.Id });
        return t;
    }

    [Fact]
    public void LayerCondition_SkipsWholeLayer_WhenFalse()
    {
        var t = Build("{Promo} == \"yes\"");
        var off = ZplRenderer.Render(t, new Dictionary<string, string> { ["Promo"] = "no" });
        var on  = ZplRenderer.Render(t, new Dictionary<string, string> { ["Promo"] = "yes" });

        Assert.Contains("ALWAYS", off);
        Assert.DoesNotContain("PROMO-ONLY", off);
        Assert.Contains("ALWAYS", on);
        Assert.Contains("PROMO-ONLY", on);
    }

    [Fact]
    public void LayerCondition_QuotedNumericForm_NowWorks()
    {
        // The exact clause shape the UI used to produce for a numeric layer condition.
        var t = Build("{Qty} > \"10\"");
        var below = ZplRenderer.Render(t, new Dictionary<string, string> { ["Qty"] = "5" });
        var above = ZplRenderer.Render(t, new Dictionary<string, string> { ["Qty"] = "15" });

        Assert.DoesNotContain("PROMO-ONLY", below);
        Assert.Contains("PROMO-ONLY", above);   // was ALWAYS skipped before the fix
    }

    [Fact]
    public void BlankLayerCondition_AlwaysPrints()
    {
        var t = Build(null);
        var zpl = ZplRenderer.Render(t, new Dictionary<string, string>());
        Assert.Contains("PROMO-ONLY", zpl);
    }
}

public class RoutingOperatorTests
{
    private static Dictionary<string, string> Row(string key, string val) =>
        new(StringComparer.OrdinalIgnoreCase) { [key] = val };

    [Fact]
    public void StartsWith_Matches()
    {
        var r = new TemplateRoute { Field = "Name", Operator = RouteOperator.StartsWith, Value = "DT-", TemplateName = "DoorTreats" };
        Assert.True(TemplateRouter.Matches(r, Row("Name", "dt-50 Chocolate")));   // case-insensitive
        Assert.False(TemplateRouter.Matches(r, Row("Name", "X-DT-50")));          // begins-with, not contains
        Assert.False(TemplateRouter.Matches(r, Row("Name", "")));
    }

    [Fact]
    public void EndsWith_Matches()
    {
        var r = new TemplateRoute { Field = "Sku", Operator = RouteOperator.EndsWith, Value = "-XL", TemplateName = "Big" };
        Assert.True(TemplateRouter.Matches(r, Row("Sku", "GIFT-XL")));
        Assert.False(TemplateRouter.Matches(r, Row("Sku", "GIFT-XL-2")));
    }

    [Fact]
    public void StartsWith_RoutesInPrecedenceOrder()
    {
        var routes = new[]
        {
            new TemplateRoute { Field = "Name", Operator = RouteOperator.StartsWith, Value = "DT-", TemplateName = "DoorTreats" },
            new TemplateRoute { Field = "Size", Operator = RouteOperator.Equals, Value = "Large", TemplateName = "Big" },
        };
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["Name"] = "DT-123", ["Size"] = "Large" };
        // Both match — the FIRST rule wins.
        Assert.Equal("DoorTreats", TemplateRouter.ResolveName(row, routes, null));
    }
}
