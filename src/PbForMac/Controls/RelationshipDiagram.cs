using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PbForMac.ViewModels;

namespace PbForMac.Controls;

/// <summary>
/// Диаграмма связей: карточки таблиц на Canvas, линии «многие → один», перетаскивание карточек.
/// </summary>
public sealed class RelationshipDiagram : UserControl
{
    public static readonly StyledProperty<ObservableCollection<DiagramTableViewModel>?> TablesProperty =
        AvaloniaProperty.Register<RelationshipDiagram, ObservableCollection<DiagramTableViewModel>?>(nameof(Tables));

    public static readonly StyledProperty<ObservableCollection<DiagramLinkViewModel>?> LinksProperty =
        AvaloniaProperty.Register<RelationshipDiagram, ObservableCollection<DiagramLinkViewModel>?>(nameof(Links));

    public static readonly StyledProperty<DiagramLinkViewModel?> SelectedLinkProperty =
        AvaloniaProperty.Register<RelationshipDiagram, DiagramLinkViewModel?>(nameof(SelectedLink),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<Action<DiagramTableViewModel, double, double>?> PositionChangedProperty =
        AvaloniaProperty.Register<RelationshipDiagram, Action<DiagramTableViewModel, double, double>?>(nameof(PositionChanged));

    public static readonly StyledProperty<Action<DiagramTableViewModel, DiagramColumnViewModel>?> ColumnClickedProperty =
        AvaloniaProperty.Register<RelationshipDiagram, Action<DiagramTableViewModel, DiagramColumnViewModel>?>(nameof(ColumnClicked));

    private readonly Canvas _surface = new() { Background = Brushes.Transparent };
    private readonly Canvas _linesLayer = new();
    private readonly Canvas _cardsLayer = new();
    private readonly Dictionary<DiagramTableViewModel, Border> _cards = [];

    private DiagramTableViewModel? _dragTable;
    private Point _dragStart;
    private Point _dragOrigin;

    public RelationshipDiagram()
    {
        _surface.Children.Add(_linesLayer);
        _surface.Children.Add(_cardsLayer);
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _surface,
            Background = Brushes.Transparent,
        };
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => _dragTable = null;
    }

    public ObservableCollection<DiagramTableViewModel>? Tables
    {
        get => GetValue(TablesProperty);
        set => SetValue(TablesProperty, value);
    }

    public ObservableCollection<DiagramLinkViewModel>? Links
    {
        get => GetValue(LinksProperty);
        set => SetValue(LinksProperty, value);
    }

    public DiagramLinkViewModel? SelectedLink
    {
        get => GetValue(SelectedLinkProperty);
        set => SetValue(SelectedLinkProperty, value);
    }

    public Action<DiagramTableViewModel, double, double>? PositionChanged
    {
        get => GetValue(PositionChangedProperty);
        set => SetValue(PositionChangedProperty, value);
    }

    public Action<DiagramTableViewModel, DiagramColumnViewModel>? ColumnClicked
    {
        get => GetValue(ColumnClickedProperty);
        set => SetValue(ColumnClickedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TablesProperty)
        {
            if (change.OldValue is ObservableCollection<DiagramTableViewModel> oldTables)
                oldTables.CollectionChanged -= OnTablesChanged;
            if (change.NewValue is ObservableCollection<DiagramTableViewModel> newTables)
                newTables.CollectionChanged += OnTablesChanged;
            RebuildCards();
            RebuildLinks();
        }
        else if (change.Property == LinksProperty)
        {
            if (change.OldValue is ObservableCollection<DiagramLinkViewModel> oldLinks)
                oldLinks.CollectionChanged -= OnLinksChanged;
            if (change.NewValue is ObservableCollection<DiagramLinkViewModel> newLinks)
                newLinks.CollectionChanged += OnLinksChanged;
            RebuildLinks();
        }
        else if (change.Property == SelectedLinkProperty)
            RebuildLinks();
    }

    private void OnTablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildCards();
        RebuildLinks();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildLinks();

    private void RebuildCards()
    {
        foreach (var table in _cards.Keys.ToList())
            table.PropertyChanged -= OnTablePropertyChanged;
        _cardsLayer.Children.Clear();
        _cards.Clear();
        if (Tables is null)
        {
            UpdateSurfaceSize();
            return;
        }

        foreach (var table in Tables)
        {
            var card = BuildCard(table);
            _cards[table] = card;
            _cardsLayer.Children.Add(card);
            Canvas.SetLeft(card, table.X);
            Canvas.SetTop(card, table.Y);
            table.PropertyChanged += OnTablePropertyChanged;
        }
        UpdateSurfaceSize();
    }

    private void OnTablePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DiagramTableViewModel table || !_cards.TryGetValue(table, out var card))
            return;
        if (e.PropertyName is nameof(DiagramTableViewModel.X) or nameof(DiagramTableViewModel.Y))
        {
            Canvas.SetLeft(card, table.X);
            Canvas.SetTop(card, table.Y);
            foreach (var link in Links ?? [])
            {
                if (link.From == table || link.To == table)
                    link.Recalculate();
            }
            RebuildLinks();
            UpdateSurfaceSize();
        }
        else if (e.PropertyName == nameof(DiagramTableViewModel.IsHighlighted))
        {
            card.BorderBrush = Brush("SelectionBrush", Brushes.DodgerBlue);
            card.BorderThickness = new Thickness(table.IsHighlighted ? 2 : 1);
            if (!table.IsHighlighted)
                card.BorderBrush = Brush("DividerBrush", Brushes.Gray);
        }
    }

    private Border BuildCard(DiagramTableViewModel table)
    {
        var header = new Border
        {
            Background = Brush("BrandBrush", Brushes.Gold),
            Padding = new Thickness(10, 8),
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = new TextBlock
            {
                Text = table.Name,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brushes.Black,
            },
        };
        header.PointerPressed += (_, e) => BeginDrag(table, e);

        var rows = new StackPanel();
        foreach (var column in table.Columns)
        {
            var glyph = new TextBlock
            {
                Text = column.TypeGlyph,
                FontSize = 11,
                Foreground = Brush("SubtleTextBrush", Brushes.Gray),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = column.Name,
                FontSize = 12,
                FontWeight = column.IsKey ? FontWeight.SemiBold : FontWeight.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 1);
            var row = new Border
            {
                Padding = new Thickness(8, 3),
                Background = column.IsKey ? Brush("InfoBackgroundBrush", Brushes.LightBlue) : Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid
                {
                    ColumnDefinitions = ColumnDefinitions.Parse("16,*"),
                    Children = { glyph, label },
                },
            };
            var captured = column;
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    return;
                ColumnClicked?.Invoke(table, captured);
                e.Handled = true;
            };
            rows.Children.Add(row);
        }

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(rows);

        return new Border
        {
            Width = table.Width,
            Background = Brush("PageBrush", Brushes.White),
            BorderBrush = table.IsHighlighted ? Brush("SelectionBrush", Brushes.DodgerBlue) : Brush("DividerBrush", Brushes.Gray),
            BorderThickness = new Thickness(table.IsHighlighted ? 2 : 1),
            CornerRadius = new CornerRadius(6),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 8,
                OffsetY = 2,
                Color = Color.FromArgb(40, 0, 0, 0),
            }),
            Child = panel,
        };
    }

    private void RebuildLinks()
    {
        _linesLayer.Children.Clear();
        if (Links is null)
            return;

        foreach (var link in Links)
        {
            link.Recalculate();
            var selected = ReferenceEquals(link, SelectedLink) || link.IsSelected;
            var color = link.HasIssue
                ? Brush("ErrorBrush", Brushes.Crimson)
                : selected
                    ? Brush("SelectionBrush", Brushes.DodgerBlue)
                    : Brush("SubtleTextBrush", Brushes.Gray);

            var curve = BuildCurve(link.X1, link.Y1, link.X2, link.Y2);
            var path = new Avalonia.Controls.Shapes.Path { Stroke = color, StrokeThickness = selected ? 2.5 : 1.5, Data = curve };
            var hit = new Avalonia.Controls.Shapes.Path
            {
                Stroke = Brushes.Transparent,
                StrokeThickness = 12,
                Data = curve,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            var captured = link;
            hit.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    return;
                SelectedLink = captured;
                e.Handled = true;
            };
            ToolTip.SetTip(hit, link.Tooltip);

            var arrow = new Polygon { Points = ArrowHead(link.X1, link.Y1, link.X2, link.Y2), Fill = color };

            var badge = new Border
            {
                Background = Brush("PageBrush", Brushes.White),
                BorderBrush = color,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = link.Label, FontSize = 11, Foreground = color },
            };
            ToolTip.SetTip(badge, link.Tooltip);
            Canvas.SetLeft(badge, link.MidX);
            Canvas.SetTop(badge, link.MidY);
            badge.PointerPressed += (_, e) =>
            {
                SelectedLink = captured;
                e.Handled = true;
            };

            _linesLayer.Children.Add(path);
            _linesLayer.Children.Add(hit);
            _linesLayer.Children.Add(arrow);
            _linesLayer.Children.Add(badge);
        }
    }

    private static Geometry BuildCurve(double x1, double y1, double x2, double y2)
    {
        var dx = Math.Abs(x2 - x1) * 0.45;
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(x1, y1), false);
        ctx.CubicBezierTo(
            new Point(x1 + (x2 >= x1 ? dx : -dx), y1),
            new Point(x2 - (x2 >= x1 ? dx : -dx), y2),
            new Point(x2, y2));
        ctx.EndFigure(false);
        return geometry;
    }

    private static Points ArrowHead(double x1, double y1, double x2, double y2)
    {
        var angle = Math.Atan2(y2 - y1, x2 - x1);
        const double size = 8;
        return
        [
            new Point(x2, y2),
            new Point(x2 - size * Math.Cos(angle - 0.4), y2 - size * Math.Sin(angle - 0.4)),
            new Point(x2 - size * Math.Cos(angle + 0.4), y2 - size * Math.Sin(angle + 0.4)),
        ];
    }

    private void BeginDrag(DiagramTableViewModel table, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _dragTable = table;
        _dragStart = e.GetPosition(_surface);
        _dragOrigin = new Point(table.X, table.Y);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragTable is null)
            return;
        var delta = e.GetPosition(_surface) - _dragStart;
        _dragTable.X = Math.Max(0, Math.Round((_dragOrigin.X + delta.X) / 10) * 10);
        _dragTable.Y = Math.Max(0, Math.Round((_dragOrigin.Y + delta.Y) / 10) * 10);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragTable is null)
            return;
        PositionChanged?.Invoke(_dragTable, _dragTable.X, _dragTable.Y);
        _dragTable = null;
    }

    private void UpdateSurfaceSize()
    {
        double width = 800, height = 500;
        if (Tables is { Count: > 0 })
        {
            width = Math.Max(width, Tables.Max(t => t.X + t.Width) + 80);
            height = Math.Max(height, Tables.Max(t => t.Y + t.Height) + 80);
        }
        _surface.Width = width;
        _surface.Height = height;
    }

    private static IBrush Brush(string key, IBrush fallback) =>
        Application.Current?.FindResource(key) as IBrush ?? fallback;
}
