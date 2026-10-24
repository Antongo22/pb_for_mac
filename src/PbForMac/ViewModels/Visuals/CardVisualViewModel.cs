using CommunityToolkit.Mvvm.ComponentModel;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.ViewModels.Visuals;

/// <summary>Карточка KPI: одно агрегированное значение.</summary>
public sealed partial class CardVisualViewModel : VisualViewModel
{
    public CardVisualViewModel(VisualDefinition definition, ReportViewModel owner) : base(definition, owner)
    {
    }

    public override bool ShowsCategory => false;
    public override bool ShowsTopN => false;
    public override string ValuesCaption => "Поле";

    public override string AutoTitle => Definition.ValueFields.Count == 0
        ? "Количество строк"
        : MeasureRef.IsMeasure(Definition.ValueFields[0])
            ? MeasureRef.Display(Definition.ValueFields[0])
            : $"{Aggregation.Label}: {FieldRef.Display(Definition.ValueFields[0])}";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private string _caption = "";

    protected override void RefreshCore()
    {
        var table = GetTable();
        if (table is null)
        {
            Message = "Выберите таблицу";
            return;
        }

        var valueField = Definition.ValueFields.FirstOrDefault();
        if (MeasureRef.TryParse(valueField, out var measureName))
        {
            try
            {
                var engine = new MeasureEngine(Owner.Model, Owner.Query);
                var result = engine.Evaluate(measureName, Owner.ActiveFilters(this));
                Value = ValueFormatter.Compact(result);
                Caption = measureName;
            }
            catch (Exception e)
            {
                Message = e.Message;
            }
            return;
        }

        var rows = GetRows(table).ToList();
        if (Field(table, valueField) is not { } field)
        {
            Value = ValueFormatter.Compact(rows.Count);
            Caption = "строк";
            return;
        }

        var value = QueryEngine.Aggregate(rows.Select(field.Get), Aggregation.Value);
        Value = ValueFormatter.Compact(value);
        Caption = $"{Aggregation.Label.ToLowerInvariant()} · {field.Name}";
    }
}
