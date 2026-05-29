using LabelDesigner.Services;
using Xunit;

namespace LabelDesigner.Tests;

public class ConditionEvaluatorTests
{
    private static Dictionary<string, string> Fields(params (string, string)[] kv)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Fact]
    public void EmptyCondition_IsTrue()
        => Assert.True(ConditionEvaluator.Evaluate(null, Fields()));

    [Fact]
    public void StringEquality()
    {
        var f = Fields(("Color", "Red"));
        Assert.True(ConditionEvaluator.Evaluate("{Color} == \"red\"", f));   // case-insensitive
        Assert.False(ConditionEvaluator.Evaluate("{Color} == \"Blue\"", f));
        Assert.True(ConditionEvaluator.Evaluate("{Color} != \"Blue\"", f));
    }

    [Fact]
    public void NumericComparison()
    {
        var f = Fields(("Qty", "5"));
        Assert.True(ConditionEvaluator.Evaluate("{Qty} >= 5", f));
        Assert.True(ConditionEvaluator.Evaluate("{Qty} > 4", f));
        Assert.False(ConditionEvaluator.Evaluate("{Qty} < 5", f));
    }

    [Fact]
    public void And_BackwardCompatible()
    {
        var f = Fields(("A", "x"), ("B", "y"));
        Assert.True(ConditionEvaluator.Evaluate("{A} == \"x\" && {B} == \"y\"", f));
        Assert.False(ConditionEvaluator.Evaluate("{A} == \"x\" && {B} == \"z\"", f));
    }

    [Fact]
    public void Or()
    {
        var f = Fields(("Color", "Blue"));
        Assert.True(ConditionEvaluator.Evaluate("{Color} == \"Red\" || {Color} == \"Blue\"", f));
        Assert.False(ConditionEvaluator.Evaluate("{Color} == \"Red\" || {Color} == \"Green\"", f));
    }

    [Fact]
    public void Parentheses_And_Precedence()
    {
        var f = Fields(("Color", "Blue"), ("Size", "L"));
        // (Red OR Blue) AND L  → true
        Assert.True(ConditionEvaluator.Evaluate("({Color} == \"Red\" || {Color} == \"Blue\") && {Size} == \"L\"", f));
        // (Red OR Green) AND L → false
        Assert.False(ConditionEvaluator.Evaluate("({Color} == \"Red\" || {Color} == \"Green\") && {Size} == \"L\"", f));
    }

    [Fact]
    public void NotGroup()
    {
        var f = Fields(("Color", "Red"));
        Assert.False(ConditionEvaluator.Evaluate("!({Color} == \"Red\")", f));
        Assert.True(ConditionEvaluator.Evaluate("!({Color} == \"Blue\")", f));
    }

    [Fact]
    public void FieldToField()
    {
        var f = Fields(("A", "x"), ("B", "x"), ("C", "y"));
        Assert.True(ConditionEvaluator.Evaluate("{A} == {B}", f));
        Assert.False(ConditionEvaluator.Evaluate("{A} == {C}", f));
        Assert.True(ConditionEvaluator.Evaluate("{A} != {C}", f));
    }

    [Fact]
    public void TruthyAndFalsy()
    {
        var f = Fields(("Set", "value"), ("Empty", ""));
        Assert.True(ConditionEvaluator.Evaluate("{Set}", f));
        Assert.False(ConditionEvaluator.Evaluate("{Empty}", f));
        Assert.True(ConditionEvaluator.Evaluate("!{Empty}", f));
        Assert.False(ConditionEvaluator.Evaluate("!{Set}", f));
    }
}
