using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Models;
using PbForMac.Services;
using PbForMac.ViewModels;
using PbForMac.ViewModels.Visuals;
using PbForMac.Views;

namespace PbForMac.Tests;

/// <summary>Клики мышью по плиткам дашборда в настоящем ReportView (Avalonia.Headless).</summary>
public class ReportViewUiTests
{
    private static (Window Window, ReportViewModel Report) ShowReport(VisualKind kind)
    {
        var model = new DataModel();
        model.AddSource(new DataSourceDefinition { TableName = "Продажи" }, TestData.Sales());
        var report = new ReportViewModel(model, new RelayCommand(() => { }), new RelayCommand(() => { }), new RelayCommand(() => { }));
        report.VisualKinds.First(k => k.Kind == kind).Add();

        var window = new Window { Width = 1400, Height = 900, Content = new ReportView { DataContext = report } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, report);
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button TileButton(Window window, string tip) =>
        window.GetVisualDescendants().OfType<VisualTileView>().Single()
            .GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) as string == tip);

    [AvaloniaFact]
    public void DeleteButtonOnTile_RemovesVisual()
    {
        var (window, report) = ShowReport(VisualKind.Card);

        Click(window, TileButton(window, "Удалить"));

        Assert.Empty(report.Visuals);
    }

    [AvaloniaFact]
    public void DuplicateButtonOnTile_AddsCopy()
    {
        var (window, report) = ShowReport(VisualKind.Card);

        Click(window, TileButton(window, "Дублировать"));

        Assert.Equal(2, report.Visuals.Count);
    }

    [AvaloniaFact]
    public void SlicerCheckBox_SelectsValue()
    {
        var (window, report) = ShowReport(VisualKind.Slicer);
        var checkBox = window.GetVisualDescendants().OfType<VisualTileView>().Single()
            .GetVisualDescendants().OfType<CheckBox>().First();

        Click(window, checkBox);

        var slicer = Assert.IsType<SlicerVisualViewModel>(Assert.Single(report.Visuals));
        Assert.Single(slicer.Definition.SelectedValues);
    }

    [AvaloniaFact]
    public void DeleteKey_AfterClickingTile_RemovesVisual()
    {
        var (window, report) = ShowReport(VisualKind.Card);
        var tile = window.GetVisualDescendants().OfType<VisualTileView>().Single();

        Click(window, tile);
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(report.Visuals);
    }
}
