using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PbForMac.ViewModels;

namespace PbForMac.Views;

public partial class ReportView : UserControl
{
    public ReportView()
    {
        InitializeComponent();
        // Клик по пустому месту холста снимает выделение (плитки помечают событие обработанным).
        Board.PointerPressed += (_, _) =>
        {
            (DataContext as ReportViewModel)?.Select(null);
            Focus();
        };
        Scroller.SizeChanged += (_, _) => UpdateFitZoom();
        // Ручное изменение масштаба отключает подгонку по ширине.
        ZoomSlider.AddHandler(PointerPressedEvent, (_, _) =>
        {
            if (DataContext is ReportViewModel vm)
                vm.FitToWidth = false;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ReportViewModel vm)
            return;
        vm.ExportBoard = ExportBoardAsync;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ReportViewModel.BoardWidth) or nameof(ReportViewModel.FitToWidth))
                UpdateFitZoom();
        };
        UpdateFitZoom();
    }

    /// <summary>В режиме «По ширине» страница масштабируется под ширину видимой области.</summary>
    private void UpdateFitZoom()
    {
        if (DataContext is not ReportViewModel { FitToWidth: true } vm || Scroller.Bounds.Width <= 0)
            return;
        var available = Scroller.Bounds.Width - 48 - 14; // поля холста и полоса прокрутки
        vm.Zoom = Math.Clamp(Math.Floor(available / vm.BoardWidth * 100) / 100, 0.3, 1);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back && e.Source is not TextBox && DataContext is ReportViewModel vm)
        {
            vm.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Рендерит холст в PNG с двойной плотностью пикселей.</summary>
    private async Task ExportBoardAsync(string path)
    {
        // Даём интерфейсу перерисоваться без рамки выделения.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        const double scale = 2;
        var size = new PixelSize((int)(Board.Bounds.Width * scale), (int)(Board.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(Board);
        bitmap.Save(path);
    }
}
