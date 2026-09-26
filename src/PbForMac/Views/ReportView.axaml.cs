using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PbForMac.ViewModels;
using PbForMac.ViewModels.Visuals;

namespace PbForMac.Views;

public partial class ReportView : UserControl
{
    private static readonly DataFormat<string> FieldDragFormat =
        DataFormat.CreateStringApplicationFormat("pbformac-field");

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

        AddHandler(DragDrop.DragOverEvent, OnFieldWellDragOver);
        AddHandler(DragDrop.DropEvent, OnFieldWellDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnFieldWellDragLeave);
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

    private void OnFieldPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not FieldColumnItem field)
            return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        var start = e.GetPosition(this);
        e.Pointer.Capture(control);
        var dragged = false;

        async void OnMoved(object? _, PointerEventArgs pe)
        {
            var delta = pe.GetPosition(this) - start;
            if (dragged || Math.Abs(delta.X) + Math.Abs(delta.Y) < 6)
                return;
            dragged = true;
            pe.Pointer.Capture(null);
            control.PointerMoved -= OnMoved;
            control.PointerReleased -= OnReleased;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(FieldDragFormat, $"{field.Table}\u001F{field.Name}"));
            await DragDrop.DoDragDropAsync(pe, transfer, DragDropEffects.Copy);
        }

        void OnReleased(object? _, PointerReleasedEventArgs pe)
        {
            pe.Pointer.Capture(null);
            control.PointerMoved -= OnMoved;
            control.PointerReleased -= OnReleased;
            if (!dragged)
                field.Assign();
        }

        control.PointerMoved += OnMoved;
        control.PointerReleased += OnReleased;
        e.Handled = true;
    }

    private void OnFieldWellDragOver(object? sender, DragEventArgs e)
    {
        var well = FindFieldWell(e.Source as Visual);
        if (well is null || !e.DataTransfer.Contains(FieldDragFormat)
            || DataContext is not ReportViewModel { SelectedVisual: not null })
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        well.Classes.Set("dropTarget", true);
        e.Handled = true;
    }

    private void OnFieldWellDragLeave(object? sender, DragEventArgs e)
    {
        var well = FindFieldWell(e.Source as Visual);
        well?.Classes.Set("dropTarget", false);
    }

    private void OnFieldWellDrop(object? sender, DragEventArgs e)
    {
        var well = FindFieldWell(e.Source as Visual);
        well?.Classes.Set("dropTarget", false);
        if (well is null || DataContext is not ReportViewModel { SelectedVisual: { } visual })
            return;
        var payload = e.DataTransfer.TryGetValue(FieldDragFormat);
        if (string.IsNullOrEmpty(payload))
            return;
        var parts = payload.Split('\u001F');
        if (parts.Length != 2)
            return;
        if (well.Tag as string == "category")
            visual.AssignFieldToCategory(parts[0], parts[1]);
        else
            visual.AssignFieldToValues(parts[0], parts[1]);
        e.Handled = true;
    }

    private static Border? FindFieldWell(Visual? source)
    {
        for (var v = source; v is not null; v = v.GetVisualParent())
        {
            if (v is Border { Classes: var c } border && c.Contains("fieldWell")
                && border.Tag is "category" or "values")
                return border;
        }
        return null;
    }
}
