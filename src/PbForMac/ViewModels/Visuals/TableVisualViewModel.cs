using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using PbForMac.Controls;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels.Visuals;

/// <summary>
/// Таблица на дашборде: с полем группировки — сводная таблица агрегатов,
/// без него — строки с выбранными столбцами.
/// </summary>
public sealed partial class TableVisualViewModel : VisualViewModel
{
    private const int MaxRows = 2000;

    public TableVisualViewModel(VisualDefinition definition, ReportViewModel owner) : base(definition, owner)
    {
    }

    public override string CategoryCaption => "Строки";
    public override string ValuesCaption => "Значения";

    public override string AutoTitle => CategoryField is null ? Table ?? "Таблица" : base.AutoTitle;

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

        var rows = GetRows(table);
        DataTable result;
        if (CategoryField is null)
        {
            var fields = (Definition.ValueFields.Count > 0
                    ? Definition.ValueFields.Select(f => Field(table, f)).OfType<ResolvedField>()
                    : table.Columns.Cast<DataColumn>().Select(ResolvedField.FromColumn))
                .ToArray();
            result = new DataTable(table.TableName);
            foreach (var field in fields)
                result.Columns.Add(QueryEngine.Unique(result, field.Name), field.DataType);
            foreach (var row in rows.Take(MaxRows))
                result.Rows.Add(fields.Select(f => f.Get(row) ?? DBNull.Value).ToArray());
        }
        else
        {
            var category = RequireField(table, CategoryField);
            var valueFields = Definition.ValueFields.Select(f => Field(table, f)).OfType<ResolvedField>().ToList();
            var aggregated = QueryEngine.AggregateBy(rows, category, valueFields,
                Aggregation.Value, Granularity.Value, Definition.TopN);
            result = new DataTable(table.TableName);
            result.Columns.Add(category.Name, typeof(string));
            foreach (var name in aggregated.SeriesNames)
                result.Columns.Add(QueryEngine.Unique(result, name), typeof(double));
            for (var i = 0; i < aggregated.Categories.Count; i++)
            {
                var values = new object[result.Columns.Count];
                values[0] = aggregated.Categories[i];
                for (var s = 0; s < aggregated.Series.Count; s++)
                    values[s + 1] = double.IsNaN(aggregated.Series[s][i]) ? DBNull.Value : aggregated.Series[s][i];
                result.Rows.Add(values);
            }
        }

        Slice = new TableSlice(result, result.Rows.Cast<DataRow>().ToList());
    }
}
