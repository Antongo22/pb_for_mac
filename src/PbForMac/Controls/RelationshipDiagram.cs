using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using PbForMac.ViewModels;

namespace PbForMac.Controls;

/// <summary>
/// Диаграмма связей: бесконечный холст с масштабом, карточки таблиц и линии «многие → один».
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

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<RelationshipDiagram, double>(nameof(Zoom), 1.0);

    private const double MinZoom = 0.25;
    private const double MaxZoom = 2.5;
    private const double ZoomStep = 0.1;
    /// <summary>Запас вокруг содержимого — «бесконечная» доска для раскладки.</summary>
    private const double BoardMargin = 2400;

    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        Background = Brushes.Transparent,
    };
    private readonly LayoutTransformControl _zoomHost = new();
    private readonly Canvas _surface = new() { Background = Brushes.Transparent };
    private readonly Canvas _linesLayer = new();
    private readonly Canvas _cardsLayer = new();
    private readonly TextBlock _zoomLabel = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center };
    private readonly Dictionary<DiagramTableViewModel, Border> _cards = [];

    private DiagramTableViewModel? _dragTable;
    private Point _dragStart;
    private Point _dragOrigin;
    private double _originX;
    private double _originY;
    private bool _panning;
    private Point _panStart;
    private Vector _panOffsetStart;

    public RelationshipDiagram()
    {
        _surface.Children.Add(_linesLayer);
        _surface.Children.Add(_cardsLayer);
        _zoomHost.Child = _surface;
        _scroll.Content = _zoomHost;

        var root = new Grid();
        root.Children.Add(_scroll);
        root.Children.Add(BuildZoomBar());
        Content = root;

        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) =>
        {
            _dragTable = null;
            _panning = false;
        };
        AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _scroll.PointerPressed += OnScrollPointerPressed;
        ActualThemeVariantChanged += (_, _) =>
        {
            RebuildCards();
            RebuildLinks();
        };
        UpdateZoomTransform();
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

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, Math.Clamp(value, MinZoom, MaxZoom));
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
        else if (change.Property == ZoomProperty)
            UpdateZoomTransform();
    }

    private Control BuildZoomBar()
    {
        var minus = ToolButton("−", () => ZoomBy(-ZoomStep));
        var plus = ToolButton("+", () => ZoomBy(ZoomStep));
        var reset = ToolButton("100%", ResetZoom);
        reset.MinWidth = 52;
        _zoomLabel.Text = "100%";

        var bar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(12),
            Padding = new Thickness(6, 4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { minus, _zoomLabel, plus, reset },
            },
        };
        ApplyChrome(bar);
        ActualThemeVariantChanged += (_, _) => ApplyChrome(bar);
        return bar;
    }

    private void ApplyChrome(Border bar)
    {
        bar.Background = ThemeBrush("PageBrush", Color.Parse("#FFFFFF"), Color.Parse("#252423"));
        bar.BorderBrush = ThemeBrush("DividerBrush", Color.Parse("#E1DFDD"), Color.Parse("#3B3A39"));
        _zoomLabel.Foreground = ThemeBrush("BodyTextBrush", Color.Parse("#201F1E"), Color.Parse("#F3F2F1"));
    }

    private static Button ToolButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(8, 2),
            MinWidth = 28,
            MinHeight = 28,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void ZoomBy(double delta) => Zoom = Math.Round((Zoom + delta) * 100) / 100;

    private void ResetZoom() => Zoom = 1;

    private void UpdateZoomTransform()
    {
        var z = Zoom;
        _zoomHost.LayoutTransform = new ScaleTransform(z, z);
        _zoomLabel.Text = $"{z * 100:0}%";
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            return;
        e.Handled = true;
        ZoomBy(e.Delta.Y > 0 ? ZoomStep : -ZoomStep);
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
        UpdateBoardMetrics();
        if (Tables is null)
            return;

        foreach (var table in Tables)
        {
            var card = BuildCard(table);
            _cards[table] = card;
            _cardsLayer.Children.Add(card);
            PlaceCard(table, card);
            table.PropertyChanged += OnTablePropertyChanged;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToContent, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void PlaceCard(DiagramTableViewModel table, Border card)
    {
        Canvas.SetLeft(card, table.X - _originX);
        Canvas.SetTop(card, table.Y - _originY);
    }

    private void OnTablePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DiagramTableViewModel table || !_cards.TryGetValue(table, out var card))
            return;
        if (e.PropertyName is nameof(DiagramTableViewModel.X) or nameof(DiagramTableViewModel.Y))
        {
            UpdateBoardMetrics();
            foreach (var (t, c) in _cards)
                PlaceCard(t, c);
            foreach (var link in Links ?? [])
            {
                if (link.From == table || link.To == table)
                    link.Recalculate();
            }
            RebuildLinks();
        }
        else if (e.PropertyName == nameof(DiagramTableViewModel.IsHighlighted))
        {
            card.BorderBrush = ThemeBrush("SelectionBrush", Color.Parse("#118DFF"), Color.Parse("#118DFF"));
            card.BorderThickness = new Thickness(table.IsHighlighted ? 2 : 1);
            if (!table.IsHighlighted)
                card.BorderBrush = ThemeBrush("DividerBrush", Color.Parse("#E1DFDD"), Color.Parse("#3B3A39"));
        }
    }

    private Border BuildCard(DiagramTableViewModel table)
    {
        var body = ThemeBrush("BodyTextBrush", Color.Parse("#201F1E"), Color.Parse("#F3F2F1"));
        var subtle = ThemeBrush("SubtleTextBrush", Color.Parse("#605E5C"), Color.Parse("#A19F9D"));
        var page = ThemeBrush("PageBrush", Color.Parse("#FFFFFF"), Color.Parse("#252423"));
        var info = ThemeBrush("InfoBackgroundBrush", Color.Parse("#E6F2FB"), Color.Parse("#1F3347"));
        var divider = ThemeBrush("DividerBrush", Color.Parse("#E1DFDD"), Color.Parse("#3B3A39"));

        var header = new Border
        {
            Background = ThemeBrush("BrandBrush", Color.Parse("#F2C811"), Color.Parse("#F2C811")),
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
                Foreground = subtle,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = column.Name,
                FontSize = 12,
                FontWeight = column.IsKey ? FontWeight.SemiBold : FontWeight.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = body,
            };
            Grid.SetColumn(label, 1);
            var row = new Border
            {
                Padding = new Thickness(8, 3),
                Background = column.IsKey ? info : Brushes.Transparent,
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
            Background = page,
            BorderBrush = table.IsHighlighted
                ? ThemeBrush("SelectionBrush", Color.Parse("#118DFF"), Color.Parse("#118DFF"))
                : divider,
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
                ? ThemeBrush("ErrorBrush", Color.Parse("#A4262C"), Color.Parse("#F1707B"))
                : selected
                    ? ThemeBrush("SelectionBrush", Color.Parse("#118DFF"), Color.Parse("#118DFF"))
                    : ThemeBrush("SubtleTextBrush", Color.Parse("#605E5C"), Color.Parse("#A19F9D"));

            var x1 = link.X1 - _originX;
            var y1 = link.Y1 - _originY;
            var x2 = link.X2 - _originX;
            var y2 = link.Y2 - _originY;
            var midX = link.MidX - _originX;
            var midY = link.MidY - _originY;

            var curve = BuildCurve(x1, y1, x2, y2);
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

            var arrow = new Polygon { Points = ArrowHead(x1, y1, x2, y2), Fill = color };
            var page = ThemeBrush("PageBrush", Color.Parse("#FFFFFF"), Color.Parse("#252423"));
            var badge = new Border
            {
                Background = page,
                BorderBrush = color,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock { Text = link.Label, FontSize = 11, Foreground = color },
            };
            ToolTip.SetTip(badge, link.Tooltip);
            Canvas.SetLeft(badge, midX);
            Canvas.SetTop(badge, midY);
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

    private void OnScrollPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Средняя кнопка или Alt+ЛКМ — панорамирование пустой области.
        var point = e.GetCurrentPoint(_scroll);
        if (point.Properties.IsMiddleButtonPressed
            || (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            if (e.Source is Visual visual && visual.FindAncestorOfType<Border>(includeSelf: true) is { } border
                && _cards.ContainsValue(border))
                return;
            _panning = true;
            _panStart = e.GetPosition(_scroll);
            _panOffsetStart = _scroll.Offset;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_panning)
        {
            var delta = e.GetPosition(_scroll) - _panStart;
            _scroll.Offset = new Vector(
                Math.Max(0, _panOffsetStart.X - delta.X),
                Math.Max(0, _panOffsetStart.Y - delta.Y));
            return;
        }

        if (_dragTable is null)
            return;
        var move = e.GetPosition(_surface) - _dragStart;
        _dragTable.X = Math.Round((_dragOrigin.X + move.X) / 10) * 10;
        _dragTable.Y = Math.Round((_dragOrigin.Y + move.Y) / 10) * 10;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            return;
        }
        if (_dragTable is null)
            return;
        PositionChanged?.Invoke(_dragTable, _dragTable.X, _dragTable.Y);
        _dragTable = null;
    }

    /// <summary>
    /// Холст всегда с большим запасом вокруг карточек: можно уезжать в любую сторону без «края».
    /// </summary>
    private void UpdateBoardMetrics()
    {
        double minX = 0, minY = 0, maxX = 800, maxY = 500;
        if (Tables is { Count: > 0 })
        {
            minX = Tables.Min(t => t.X);
            minY = Tables.Min(t => t.Y);
            maxX = Tables.Max(t => t.X + t.Width);
            maxY = Tables.Max(t => t.Y + t.Height);
        }

        var prevOx = _originX;
        var prevOy = _originY;
        _originX = minX - BoardMargin;
        _originY = minY - BoardMargin;
        _surface.Width = Math.Max(800, maxX - _originX + BoardMargin);
        _surface.Height = Math.Max(500, maxY - _originY + BoardMargin);

        // Сдвиг начала координат компенсируем Offset, чтобы картинка не прыгала.
        var dOx = (_originX - prevOx) * Zoom;
        var dOy = (_originY - prevOy) * Zoom;
        if (dOx != 0 || dOy != 0)
            _scroll.Offset = new Vector(Math.Max(0, _scroll.Offset.X - dOx), Math.Max(0, _scroll.Offset.Y - dOy));
    }

    private void ScrollToContent()
    {
        if (Tables is not { Count: > 0 })
            return;
        var x = (Tables.Min(t => t.X) - _originX) * Zoom - 48;
        var y = (Tables.Min(t => t.Y) - _originY) * Zoom - 48;
        _scroll.Offset = new Vector(Math.Max(0, x), Math.Max(0, y));
    }

    private IBrush ThemeBrush(string key, Color light, Color dark)
    {
        var variant = ActualThemeVariant;
        if (Application.Current?.TryGetResource(key, variant, out var resource) == true && resource is IBrush brush)
            return brush;
        return new SolidColorBrush(variant == ThemeVariant.Dark ? dark : light);
    }
}
