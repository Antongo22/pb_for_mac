using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Models;
using PbForMac.Services;
using PbForMac.ViewModels;
using PbForMac.Views;

namespace PbForMac.Tests;

public class DesignLayoutTests
{
    [Fact]
    public void Serializer_RoundTripsShapeButtonAndPageChrome()
    {
        var report = new ReportDefinition
        {
            Pages =
            [
                new ReportPage
                {
                    Name = "Обложка",
                    Background = "#0F2747",
                    CanvasWidth = 1400,
                    CanvasHeight = 900,
                    GridStep = 8,
                    Gap = 24,
                    Visuals =
                    [
                        new VisualDefinition
                        {
                            Kind = VisualKind.Shape,
                            Shape = ShapeKind.Ellipse,
                            Fill = "#118DFF",
                            Stroke = "#FFFFFF",
                            Text = "KPI",
                            ShowTitle = false,
                            X = 40,
                            Y = 40,
                            Width = 180,
                            Height = 180,
                        },
                        new VisualDefinition
                        {
                            Kind = VisualKind.Button,
                            Text = "Далее",
                            Action = DesignAction.GoToPage,
                            ActionTarget = "Страница 2",
                            Fill = "#118DFF",
                            Foreground = "#FFFFFF",
                            Bold = true,
                        },
                    ],
                },
            ],
        };

        var restored = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));
        var page = restored.Pages[0];
        Assert.Equal("#0F2747", page.Background);
        Assert.Equal(1400, page.CanvasWidth);
        Assert.Equal(8, page.GridStep);
        Assert.Equal(VisualKind.Shape, page.Visuals[0].Kind);
        Assert.Equal(ShapeKind.Ellipse, page.Visuals[0].Shape);
        Assert.Equal("#118DFF", page.Visuals[0].Fill);
        Assert.Equal(DesignAction.GoToPage, page.Visuals[1].Action);
        Assert.Equal("Страница 2", page.Visuals[1].ActionTarget);
    }

    [AvaloniaFact]
    public void Shape_InsertsOnCanvas()
    {
        var report = new ReportViewModel(new DataModel(), new SilentDialogs(),
            new RelayCommand(() => { }), new RelayCommand(() => { }), new RelayCommand(() => { }));
        report.DesignKinds.First(k => k.Kind == VisualKind.Shape).Add();

        var window = new Window { Width = 1400, Height = 900, Content = new ReportView { DataContext = report } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var visual = Assert.Single(report.Visuals);
        Assert.Equal(VisualKind.Shape, visual.Kind);
        Assert.Equal("#F3F2F1", visual.Definition.Fill);
        Assert.False(visual.Definition.ShowTitle);
        Assert.NotNull(window.Content);
    }

    private sealed class SilentDialogs : IDialogService
    {
        public Task<IReadOnlyList<string>> OpenFilesAsync(string t, IReadOnlyList<FileTypeFilter> f, bool m) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> OpenFolderAsync(string t) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string s, FileTypeFilter f) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>?> SelectItemsAsync(string t, string m, IReadOnlyList<string> i) => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<int> ChooseAsync(string t, string m, IReadOnlyList<string> o) => Task.FromResult(-1);
        public Task<string?> PromptAsync(string t, string m, string i = "") => Task.FromResult<string?>(null);
        public Task ShowMessageAsync(string t, string m) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string t, string m) => Task.FromResult(true);
    }
}
