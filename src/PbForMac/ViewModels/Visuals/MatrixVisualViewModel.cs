using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using PbForMac.Controls;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels.Visuals;

/// <summary>Матрица: строки × столбцы × агрегированное значение (как в Power BI).</summary>
public sealed partial class MatrixVisualViewModel : VisualViewModel
{
    public MatrixVisualViewModel(VisualDefinition definition, ReportViewModel owner) : base(definition, owner)
    {
    }

    public override string CategoryCaption => "Строки";
    public override string ValuesCaption => "Значения";
    public override bool ShowsColumnField => true;

    public override string AutoTitle
    {
        get
        {
            var value = Definition.ValueFields.Count == 0
                ? "Количество строк"
                : $"{Aggregation.Label}: {FieldRef.Display(Definition.ValueFields[0])}";
            if (CategoryField is null && ColumnField is null)
                return "Матрица";
            if (CategoryField is not null && ColumnField is not null)
                return $"{value} по {FieldRef.Display(CategoryField)} × {FieldRef.Display(ColumnField)}";
            return CategoryField is not null
                ? $"{value} по {FieldRef.Display(CategoryField)}"
                : $"{value} по {FieldRef.Display(ColumnField!)}";
        }
    }

    [ObservableProperty]
    private TableSlice? _slice;

    protected override void RefreshCore()
    {
        Slice = null;
        var table = GetTable();
        if (table is null)
        {
            Message = "Выберите таблицу";
            return;
        }
        if (CategoryField is null || ColumnField is null)
        {
            Message = "Укажите поля строк и столбцов";
            return;
        }

        var rowField = RequireField(table, CategoryField);
        var colField = RequireField(table, ColumnField);
        var valueField = Definition.ValueFields.Count > 0
            ? Field(table, Definition.ValueFields[0])
            : null;

        var matrix = QueryEngine.AggregateBy2D(
            GetRows(table), rowField, colField, valueField,
            Aggregation.Value, Granularity.Value, Granularity.Value, Definition.TopN);

        var result = new DataTable(table.TableName);
        result.Columns.Add(rowField.Name, typeof(string));
        foreach (var col in matrix.ColumnLabels)
            result.Columns.Add(QueryEngine.Unique(result, col), typeof(double));

        for (var r = 0; r < matrix.RowLabels.Count; r++)
        {
            var values = new object[result.Columns.Count];
            values[0] = matrix.RowLabels[r];
            for (var c = 0; c < matrix.ColumnLabels.Count; c++)
            {
                var cell = matrix.Cells[r][c];
                values[c + 1] = double.IsNaN(cell) ? DBNull.Value : cell;
            }
            result.Rows.Add(values);
        }

        Slice = new TableSlice(result, result.Rows.Cast<DataRow>().ToList());
    }
}
