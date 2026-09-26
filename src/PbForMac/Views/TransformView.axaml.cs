using Avalonia;
using Avalonia.Controls;
using PbForMac.ViewModels;

namespace PbForMac.Views;

public partial class TransformView : UserControl
{
    public TransformView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireDiagram();
        AttachedToVisualTree += (_, _) => WireDiagram();
    }

    private void WireDiagram()
    {
        if (this.FindControl<Controls.RelationshipDiagram>("RelationshipDiagram") is not { } diagram
            || DataContext is not TransformViewModel vm)
            return;
        diagram.PositionChanged = vm.OnDiagramPositionChanged;
        diagram.ColumnClicked = vm.OnDiagramColumnClicked;
    }
}
