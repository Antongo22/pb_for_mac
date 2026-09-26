using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PbForMac.Models;
using PbForMac.Services;
using SkiaSharp;

namespace PbForMac.ViewModels.Visuals;

/// <summary>Палитра графиков в стиле Power BI.</summary>
public static class ChartPalette
{
    public static readonly SKColor[] Colors =
    [
        SKColor.Parse("#118DFF"), SKColor.Parse("#12239E"), SKColor.Parse("#E66C37"), SKColor.Parse("#6B007B"),
        SKColor.Parse("#E044A7"), SKColor.Parse("#744EC2"), SKColor.Parse("#D9B300"), SKColor.Parse("#D64550"),
        SKColor.Parse("#197278"), SKColor.Parse("#1AAB40"), SKColor.Parse("#15C6F4"), SKColor.Parse("#4092FF"),
    ];

    public static SKColor At(int index) => Colors[index % Colors.Length];

    /// <summary>Нейтральный серый, читаемый и на светлом, и на тёмном фоне.</summary>
    public static readonly SKColor Text = SKColor.Parse("#8A8886");
    public static readonly SKColor Grid = SKColor.Parse("#8A8886").WithAlpha(50);
}

/// <summary>Гистограмма, линейчатая, график, области, круговая и точечная диаграммы.</summary>
public sealed partial class ChartVisualViewModel : VisualViewModel
{
    private const int MaxPieSlices = 10;
    private const int MaxScatterPoints = 5000;

    public ChartVisualViewModel(VisualDefinition definition, ReportViewModel owner) : base(definition, owner)
    {
    }

    public bool IsPie => Kind == VisualKind.Pie;
    public bool IsCartesian => !IsPie;

    public override string CategoryCaption => Kind switch
    {
        VisualKind.Scatter => "Ось X (число)",
        VisualKind.Pie => "Легенда",
        _ => "Ось",
    };

    public override string ValuesCaption => Kind == VisualKind.Scatter ? "Ось Y (число)" : "Значения";
    public override bool ShowsAggregation => Kind != VisualKind.Scatter;
    public override bool ShowsTopN => Kind != VisualKind.Scatter;

    public override string AutoTitle => Kind == VisualKind.Scatter && CategoryField is not null && Definition.ValueFields.Count > 0
        ? $"{Definition.ValueFields[0]} от {CategoryField}"
        : base.AutoTitle;

    [ObservableProperty]
    private ISeries[] _series = [];

    [ObservableProperty]
    private ICartesianAxis[] _xAxes = [new Axis()];

    [ObservableProperty]
    private ICartesianAxis[] _yAxes = [new Axis()];

    [ObservableProperty]
    private LegendPosition _legendPosition = LegendPosition.Hidden;

    public SolidColorPaint LegendTextPaint { get; } = new(ChartPalette.Text);

    protected override void RefreshCore()
    {
        Series = [];
        var table = GetTable();
        if (table is null)
        {
            Message = "Выберите таблицу";
            return;
        }
        if (CategoryField is null)
        {
            Message = $"Выберите поле «{CategoryCaption}»";
            return;
        }

        if (Kind == VisualKind.Scatter)
        {
            BuildScatter(table);
            return;
        }

        var result = QueryEngine.AggregateBy(table, GetRows(table), CategoryField, Definition.ValueFields,
            Aggregation.Value, Granularity.Value, Definition.TopN);
        if (result.Categories.Count == 0)
        {
            Message = "Нет данных";
            return;
        }

        switch (Kind)
        {
            case VisualKind.Pie:
                BuildPie(result);
                break;
            case VisualKind.Bar:
                BuildBar(result);
                break;
            default:
                BuildCartesian(result);
                break;
        }
    }

    private void BuildCartesian(AggregatedResult result)
    {
        Series = result.Series.Select((values, i) => CreateSeries(result.SeriesNames[i], values, ChartPalette.At(i))).ToArray();
        XAxes = [CategoryAxis(result.Categories)];
        YAxes = [ValueAxis(fromZero: Kind == VisualKind.Column && MinValue(result) >= 0)];
        LegendPosition = result.Series.Count > 1 ? LegendPosition.Bottom : LegendPosition.Hidden;
    }

    private ISeries CreateSeries(string name, double[] values, SKColor color) => Kind switch
    {
        VisualKind.Line => new LineSeries<double>
        {
            Name = name,
            Values = values,
            Fill = null,
            Stroke = new SolidColorPaint(color, 3),
            GeometryStroke = new SolidColorPaint(color, 3),
            GeometryFill = new SolidColorPaint(SKColors.White),
            GeometrySize = values.Length > 40 ? 0 : 7,
            LineSmoothness = 0.2,
        },
        VisualKind.Area => new LineSeries<double>
        {
            Name = name,
            Values = values,
            Fill = new SolidColorPaint(color.WithAlpha(90)),
            Stroke = new SolidColorPaint(color, 2),
            GeometrySize = 0,
            LineSmoothness = 0.3,
        },
        _ => new ColumnSeries<double>
        {
            Name = name,
            Values = values,
            Fill = new SolidColorPaint(color),
            MaxBarWidth = 60,
            Padding = 4,
        },
    };

    private void BuildBar(AggregatedResult result)
    {
        // Линейчатая диаграмма рисуется снизу вверх — переворачиваем, чтобы первая категория была сверху.
        var categories = result.Categories.Reverse().ToArray();
        Series = result.Series.Select((values, i) => (ISeries)new RowSeries<double>
        {
            Name = result.SeriesNames[i],
            Values = values.Reverse().ToArray(),
            Fill = new SolidColorPaint(ChartPalette.At(i)),
            MaxBarWidth = 40,
            Padding = 3,
        }).ToArray();
        XAxes = [ValueAxis(fromZero: MinValue(result) >= 0)];
        YAxes = [CategoryAxis(categories, rotate: false)];
        LegendPosition = result.Series.Count > 1 ? LegendPosition.Bottom : LegendPosition.Hidden;
    }

    private void BuildPie(AggregatedResult result)
    {
        var values = result.Series[0];
        var slices = result.Categories.Select((c, i) => (Label: c, Value: values[i]))
            .Where(s => !double.IsNaN(s.Value) && s.Value > 0)
            .OrderByDescending(s => s.Value)
            .ToList();
        if (slices.Count > MaxPieSlices)
        {
            var other = slices.Skip(MaxPieSlices - 1).Sum(s => s.Value);
            slices = [.. slices.Take(MaxPieSlices - 1), ("Прочее", other)];
        }
        if (slices.Count == 0)
        {
            Message = "Нет положительных значений для круговой диаграммы";
            return;
        }

        var total = slices.Sum(s => s.Value);
        Series = slices.Select((s, i) => (ISeries)new PieSeries<double>
        {
            Name = s.Label,
            Values = [s.Value],
            Fill = new SolidColorPaint(ChartPalette.At(i)),
            InnerRadius = 40,
            DataLabelsPaint = new SolidColorPaint(SKColors.White),
            DataLabelsSize = 11,
            DataLabelsPosition = PolarLabelsPosition.Middle,
            DataLabelsFormatter = p => s.Value / total >= 0.06 ? $"{s.Value / total:P0}" : "",
            ToolTipLabelFormatter = p => $"{ValueFormatter.Number(s.Value)} ({s.Value / total:P1})",
        }).ToArray();
        LegendPosition = LegendPosition.Right;
    }

    private void BuildScatter(DataTable table)
    {
        var yField = Definition.ValueFields.FirstOrDefault();
        if (yField is null)
        {
            Message = $"Выберите поле «{ValuesCaption}»";
            return;
        }
        var points = GetRows(table)
            .Select(r => (X: QueryEngine.ToDouble(r[CategoryField!]), Y: QueryEngine.ToDouble(r[yField])))
            .Where(p => p.X.HasValue && p.Y.HasValue)
            .Take(MaxScatterPoints)
            .Select(p => new ObservablePoint(p.X, p.Y))
            .ToArray();
        if (points.Length == 0)
        {
            Message = "Для точечной диаграммы нужны числовые поля X и Y";
            return;
        }

        var color = ChartPalette.At(0);
        Series =
        [
            new ScatterSeries<ObservablePoint>
            {
                Name = yField,
                Values = points,
                Fill = new SolidColorPaint(color.WithAlpha(140)),
                Stroke = null,
                GeometrySize = 8,
            },
        ];
        XAxes = [ValueAxis(CategoryField)];
        YAxes = [ValueAxis(yField)];
        LegendPosition = LegendPosition.Hidden;
    }

    private static Axis CategoryAxis(IReadOnlyList<string> labels, bool rotate = true) => new()
    {
        Labels = labels.ToArray(),
        LabelsRotation = rotate && labels.Count > 6 ? -35 : 0,
        TextSize = 11,
        LabelsPaint = new SolidColorPaint(ChartPalette.Text),
        SeparatorsPaint = null,
        MinStep = 1,
        ForceStepToMin = labels.Count <= 30,
    };

    private static double MinValue(AggregatedResult result) =>
        result.Series.SelectMany(s => s).Where(v => !double.IsNaN(v)).DefaultIfEmpty(0).Min();

    private static Axis ValueAxis(string? name = null, bool fromZero = false) => new()
    {
        MinLimit = fromZero ? 0 : null,
        Name = name,
        NameTextSize = 11,
        NamePaint = name is null ? null : new SolidColorPaint(ChartPalette.Text),
        TextSize = 11,
        Labeler = ValueFormatter.Compact,
        LabelsPaint = new SolidColorPaint(ChartPalette.Text),
        SeparatorsPaint = new SolidColorPaint(ChartPalette.Grid, 1),
    };
}
