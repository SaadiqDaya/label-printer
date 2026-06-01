using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LabelDesigner.Behaviors;

/// <summary>
/// Attached property that restricts a <see cref="TextBox"/> to numeric input — digits, a single
/// decimal separator ('.' or ','), and an optional leading minus. Blocks bad keystrokes and pastes
/// at the point of entry so operators can't fat-finger a letter into a quantity/price field. This is
/// a UX guard only; print-time formatting/validation (PrintService.ApplyFormat, FieldValidator) is
/// still the authority. Handlers are static, so they never root the TextBox (no leak on item reuse).
/// </summary>
public static class NumericInput
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(NumericInput),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject o) => (bool)o.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject o, bool value) => o.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue)
        {
            tb.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(tb, OnPaste);
        }
        else
        {
            tb.PreviewTextInput -= OnPreviewTextInput;
            DataObject.RemovePastingHandler(tb, OnPaste);
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        e.Handled = !IsValid(Proposed(tb, e.Text));
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var tb = (TextBox)sender;
        if (e.DataObject.GetDataPresent(typeof(string))
            && e.DataObject.GetData(typeof(string)) is string pasted
            && IsValid(Proposed(tb, pasted)))
            return;
        e.CancelCommand();
    }

    /// <summary>What the text would become if the insert were applied over the current selection.</summary>
    private static string Proposed(TextBox tb, string insert)
    {
        string text = tb.Text;
        int start = tb.SelectionStart;
        int len = tb.SelectionLength;
        return text[..start] + insert + text[(start + len)..];
    }

    // Allow an in-progress partial entry: "", "-", "1.", ".5", "-12,3" are all accepted.
    private static bool IsValid(string proposed) =>
        proposed.Length == 0 || Regex.IsMatch(proposed, @"^-?\d*([.,]\d*)?$");
}
