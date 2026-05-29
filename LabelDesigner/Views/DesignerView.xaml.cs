using LabelDesigner.Designer;
using LabelDesigner.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace LabelDesigner.Views;

public partial class DesignerView : UserControl
{
    public DesignerCanvas Canvas => LabelCanvas;

    public DesignerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Ctrl+scroll wheel zooms the canvas; plain scroll is handled by the ScrollViewer normally.
    /// </summary>
    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        e.Handled = true; // prevent the ScrollViewer from scrolling

        if (DataContext is not DesignerViewModel vm) return;

        if (e.Delta > 0)
            vm.ZoomInCommand.Execute(null);
        else
            vm.ZoomOutCommand.Execute(null);
    }

    /// <summary>Enter in the Go-To box commits the value (LostFocus would also do it, but Enter is the natural gesture).</summary>
    private void GotoRowBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        // Force the binding to push to the VM right now (UpdateSourceTrigger=LostFocus by default).
        var tb = (TextBox)sender;
        var be = tb.GetBindingExpression(TextBox.TextProperty);
        be?.UpdateSource();
        e.Handled = true;
    }
}
