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
    private const double SnapStep = 10;
    private const double MinTileWidth = 160;
    private const double MinTileHeight = 100;

    private enum DragMode { None, Move, Resize }

    private DragMode _mode;
    private Point _start;
    private Rect _startRect;

    public VisualTileView()
    {
        InitializeComponent();
        // handledEventsToo: графики сами обрабатывают нажатия, но выделять плитку всё равно нужно.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        Header.PointerPressed += (s, e) => BeginDrag(DragMode.Move, e);
        Grip.PointerPressed += (s, e) => BeginDrag(DragMode.Resize, e);
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
            vm.Width = Math.Max(MinTileWidth, Snap(_startRect.Width + delta.X));
            vm.Height = Math.Max(MinTileHeight, Snap(_startRect.Height + delta.Y));
        }
    }

    private static double Snap(double value) => Math.Round(value / SnapStep) * SnapStep;
}
