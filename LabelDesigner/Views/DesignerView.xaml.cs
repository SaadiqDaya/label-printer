using LabelDesigner.Designer;
using System.Windows.Controls;

namespace LabelDesigner.Views;

public partial class DesignerView : UserControl
{
    public DesignerCanvas Canvas => LabelCanvas;

    public DesignerView()
    {
        InitializeComponent();
    }
}
