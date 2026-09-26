using System.Data;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.Tests;

public class TransformTests
{
    private static List<DataTable> Tables() => [TestData.Sales()];

    [Fact]
    public void CalculatedColumn_IsMaterializedWithInferredType()
    {
        var tables = Tables();

        TransformEngine.Apply(tables, new CalculatedColumnStep { Table = "Продажи", Name = "Сумма", Expression = "[Кол-во] * [Цена]" });

        var table = tables[0];
        Assert.Equal(typeof(double), table.Columns["Сумма"]!.DataType);
        Assert.Equal(200d, table.Rows[0]["Сумма"]);
        Assert.True(string.IsNullOrEmpty(table.Columns["Сумма"]!.Expression));

        // Столбец не зависит от исходных: их можно удалить.
        TransformEngine.Apply(tables, new RemoveColumnStep { Table = "Продажи", Column = "Цена" });
        Assert.Equal(200d, table.Rows[0]["Сумма"]);
    }

    [Fact]
    public void CalculatedColumn_InvalidExpression_ThrowsReadableError()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            TransformEngine.Apply(Tables(), new CalculatedColumnStep { Table = "Продажи", Name = "X", Expression = "[Нет такого] * 2" }));
        Assert.StartsWith("Ошибка", error.Message);
    }

    [Fact]
    public void RenameChangeTypeFilterAndDistinct()
    {
        var tables = Tables();

        TransformEngine.Apply(tables, new RenameColumnStep { Table = "Продажи", Column = "Регион", NewName = "Область" });
        TransformEngine.Apply(tables, new ChangeTypeStep { Table = "Продажи", Column = "Кол-во", TargetType = ColumnType.Text });
        TransformEngine.Apply(tables, new RemoveDuplicatesStep { Table = "Продажи" });
        TransformEngine.Apply(tables, new FilterRowsStep
        {
            Table = "Продажи",
            Filter = new FilterDefinition { Column = "Область", Operator = FilterOperator.NotEquals, Value = "Юг" },
        });

        var table = tables[0];
        Assert.True(table.Columns.Contains("Область"));
        Assert.Equal(typeof(string), table.Columns["Кол-во"]!.DataType);
        Assert.Equal(2, table.Columns["Кол-во"]!.Ordinal);
        Assert.Equal(3, table.Rows.Count);
    }

    [Fact]
    public void GroupBy_AddsNewTable()
    {
        var tables = Tables();

        TransformEngine.Apply(tables, new GroupByStep
        {
            Table = "Продажи",
            NewTable = "По регионам",
            GroupColumns = ["Регион"],
            Aggregations = [new AggregationSpec { Column = "Цена", Aggregation = Aggregation.Sum, OutputName = "Выручка" }],
        });

        Assert.Equal(2, tables.Count);
        Assert.Equal("По регионам", tables[1].TableName);
    }

    [Fact]
    public void DataModel_RecordsStepErrorsAndRemovesDependentSteps()
    {
        var model = new DataModel();
        model.AddSource(new DataSourceDefinition { TableName = "Продажи" }, TestData.Sales());
        model.AddStep(new GroupByStep
        {
            Table = "Продажи", NewTable = "Итоги", GroupColumns = ["Регион"],
            Aggregations = [new AggregationSpec { Column = "Цена", Aggregation = Aggregation.Sum }],
        });
        model.AddStep(new RenameColumnStep { Table = "Итоги", Column = "Регион", NewName = "Р" });
        Assert.Equal(2, model.Tables.Count);

        // Неверный шаг не добавляется.
        Assert.Throws<InvalidOperationException>(() => model.AddStep(new RemoveColumnStep { Table = "Продажи", Column = "Нет" }));
        Assert.Equal(2, model.Steps.Count);

        model.RemoveTable("Продажи");
        Assert.Empty(model.Steps);
        Assert.Empty(model.Tables);
    }

    [Fact]
    public void ReportSerializer_RoundTripsPolymorphicSteps()
    {
        var report = new ReportDefinition
        {
            Sources = [new DataSourceDefinition { Kind = SourceKind.Csv, Path = "/tmp/a.csv", TableName = "A" }],
            Steps =
            [
                new CalculatedColumnStep { Table = "A", Name = "S", Expression = "[x] * 2" },
                new FilterRowsStep { Table = "A", Filter = new FilterDefinition { Column = "x", Operator = FilterOperator.In, Values = ["1"] } },
            ],
            Visuals = [new VisualDefinition { Kind = VisualKind.Pie, Table = "A", ValueFields = ["x"] }],
        };

        var json = ReportSerializer.Serialize(report);
        var restored = ReportSerializer.Deserialize(json);

        Assert.Contains("\"calculated\"", json);
        Assert.IsType<CalculatedColumnStep>(restored.Steps[0]);
        Assert.Equal(["1"], ((FilterRowsStep)restored.Steps[1]).Filter.Values);
        Assert.Equal(VisualKind.Pie, restored.Visuals[0].Kind);
    }
}
