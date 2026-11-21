using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PbForMac.ViewModels.Visuals;

namespace PbForMac.Views;

/// <summary>Плитка визуала: выделение по клику, перемещение за заголовок, изменение размера за уголок.</summary>
public partial class VisualTileView : UserControl
{
    private const double MinDataWidth = 160;
    private const double MinDataHeight = 100;

    private enum DragMode { None, Move, Resize }

    private DragMode _mode;
    private Point _start;
    private Rect _startRect;

    public VisualTileView()
    {
        InitializeComponent();
        // handledEventsToo: графики сами обрабатывают нажатия, но выделять плитку всё равно нужно.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        Header.PointerPressed += (_, e) => BeginDrag(DragMode.Move, e);
        DragHandle.PointerPressed += (_, e) => BeginDrag(DragMode.Move, e);
        Grip.PointerPressed += (_, e) => BeginDrag(DragMode.Resize, e);
        AddHandler(PointerPressedEvent, OnDesignBodyPressed, RoutingStrategies.Tunnel);
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, _) => _mode = DragMode.None;
        PointerCaptureLost += (_, _) => _mode = DragMode.None;
    }

    private VisualViewModel? ViewModel => DataContext as VisualViewModel;

    private Visual? Surface => this.FindAncestorOfType<Canvas>();

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.Select();
        // Фокус на страницу отчёта (для удаления клавишей Delete/⌫) переводим только при клике по самой плитке:
        // кнопка или флажок, потерявшие фокус во время нажатия, сбрасывают его, и клик не срабатывает.
        if (!IsInsideFocusableControl(e.Source))
            this.FindAncestorOfType<ReportView>()?.Focus();
        e.Handled = true;
    }

    /// <summary>Нажатие пришлось на кнопку, флажок, поле ввода, таблицу и т. п. внутри плитки.</summary>
    private bool IsInsideFocusableControl(object? source) =>
        source is Visual visual && visual.GetSelfAndVisualAncestors()
            .TakeWhile(v => v != this)
            .Any(v => v is InputElement { Focusable: true });

    private void OnDesignBodyPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { IsDesign: true, IsButton: false } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (IsGrip(e.Source) || e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;
        BeginDrag(DragMode.Move, e);
    }

    private bool IsGrip(object? source) =>
        source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => ReferenceEquals(v, Grip));

    private void BeginDrag(DragMode mode, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } vm || Surface is not { } surface || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        _mode = mode;
        _start = e.GetPosition(surface);
        _startRect = new Rect(vm.X, vm.Y, vm.Width, vm.Height);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_mode == DragMode.None || ViewModel is not { } vm || Surface is not { } surface)
            return;

        var delta = e.GetPosition(surface) - _start;
        if (_mode == DragMode.Move)
        {
            vm.X = Math.Max(0, Snap(_startRect.X + delta.X));
            vm.Y = Math.Max(0, Snap(_startRect.Y + delta.Y));
        }
        else
        {
            var minW = vm.IsDesign ? 32 : MinDataWidth;
            var minH = vm.IsDesign ? 20 : MinDataHeight;
            vm.Width = Math.Max(minW, Snap(_startRect.Width + delta.X));
            vm.Height = Math.Max(minH, Snap(_startRect.Height + delta.Y));
        }
    }

    private double Snap(double value)
    {
        var step = ViewModel?.Owner.SnapStep ?? 10;
        if (step < 1)
            return value;
        return Math.Round(value / step) * step;
    }
}
