using System.Windows;
using System.Windows.Input;

namespace LabelDesigner.Views;

public partial class InputDialog : Window
{
    public string Value => ValueBox.Text;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptBlock.Text = prompt;
        ValueBox.Text = defaultValue;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    private void OK_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) DialogResult = true;
    }
}
