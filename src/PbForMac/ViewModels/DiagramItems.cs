using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels;

/// <summary>Карточка таблицы на диаграмме связей.</summary>
public sealed partial class DiagramTableViewModel : ObservableObject
{
    public DiagramTableViewModel(string name, IEnumerable<DiagramColumnViewModel> columns, double x, double y)
    {
        Name = name;
        foreach (var column in columns)
            Columns.Add(column);
        _x = x;
        _y = y;
    }

    public string Name { get; }
    public ObservableCollection<DiagramColumnViewModel> Columns { get; } = [];
    public double Width => DiagramLayout.CardWidth;
    public double Height => DiagramLayout.CardHeight(Columns.Count);

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private bool _isHighlighted;

    public int IndexOf(string column)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (string.Equals(Columns[i].Name, column, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }
}

/// <summary>Столбец на карточке диаграммы; ключевые подсвечиваются.</summary>
public sealed partial class DiagramColumnViewModel : ObservableObject
{
    public DiagramColumnViewModel(string name, ColumnType type, bool isKey)
    {
        Name = name;
        Type = type;
        IsKey = isKey;
    }

    public string Name { get; }
    public ColumnType Type { get; }
    public bool IsKey { get; }

    public string TypeGlyph => Type switch
    {
        ColumnType.Integer or ColumnType.Decimal => "Σ",
        ColumnType.Date => "◷",
        ColumnType.Boolean => "✓",
        _ => "A",
    };

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Связь на диаграмме: координаты линии и подпись.</summary>
public sealed partial class DiagramLinkViewModel : ObservableObject
{
    public DiagramLinkViewModel(RelationshipDefinition relationship, DiagramTableViewModel from, DiagramTableViewModel to,
        double? matchRate, string? issue)
    {
        Relationship = relationship;
        From = from;
        To = to;
        MatchRate = matchRate;
        Issue = issue;
        Recalculate();
    }

    public RelationshipDefinition Relationship { get; }
    public DiagramTableViewModel From { get; }
    public DiagramTableViewModel To { get; }
    public double? MatchRate { get; }
    public string? Issue { get; }
    public bool HasIssue => Issue is not null;

    public string Label => MatchRate is { } rate
        ? $"* : 1 · {rate:P0}"
        : "* : 1";

    public string Tooltip => HasIssue
        ? $"{Relationship}\n{Issue}"
        : $"{Relationship}\n{Label}";

    [ObservableProperty]
    private double _x1;

    [ObservableProperty]
    private double _y1;

    [ObservableProperty]
    private double _x2;

    [ObservableProperty]
    private double _y2;

    [ObservableProperty]
    private double _midX;

    [ObservableProperty]
    private double _midY;

    [ObservableProperty]
    private bool _isSelected;

    public void Recalculate()
    {
        var fromIndex = Math.Max(0, From.IndexOf(Relationship.FromColumn));
        var toIndex = Math.Max(0, To.IndexOf(Relationship.ToColumn));
        // Линия идёт от правого края факта к левому краю справочника (или наоборот, если раскладка обратная).
        var fromOnRight = From.X <= To.X;
        var (x1, y1) = DiagramLayout.ColumnAnchor(From.X, From.Y, fromIndex, rightSide: fromOnRight);
        var (x2, y2) = DiagramLayout.ColumnAnchor(To.X, To.Y, toIndex, rightSide: !fromOnRight);
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        MidX = (x1 + x2) / 2 - 28;
        MidY = (y1 + y2) / 2 - 10;
    }
}
