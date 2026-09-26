using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.Tests;

public class QueryEngineTests
{
    [Theory]
    [InlineData(Aggregation.Sum, 16)]
    [InlineData(Aggregation.Average, 3.2)]
    [InlineData(Aggregation.Count, 5)]
    [InlineData(Aggregation.DistinctCount, 4)]
    [InlineData(Aggregation.Min, 1)]
    [InlineData(Aggregation.Max, 5)]
    public void Aggregate_IgnoresEmptyValues(Aggregation aggregation, double expected)
    {
        var table = TestData.Sales();
        var result = QueryEngine.Aggregate(table.Rows.Cast<System.Data.DataRow>().Select(r => r["Кол-во"]), aggregation);
        Assert.Equal(expected, result, 6);
    }

    [Theory]
    [InlineData(FilterOperator.Equals, "Север", 2)]
    [InlineData(FilterOperator.NotEquals, "север", 4)]
    [InlineData(FilterOperator.Contains, "ю", 2)]
    [InlineData(FilterOperator.StartsWith, "За", 2)]
    [InlineData(FilterOperator.IsEmpty, null, 0)]
    public void Filter_TextOperators(FilterOperator op, string? value, int expected)
    {
        var table = TestData.Sales();
        var rows = QueryEngine.Filter(table, [new FilterDefinition { Column = "Регион", Operator = op, Value = value }]);
        Assert.Equal(expected, rows.Count());
    }

    [Fact]
    public void Filter_ComparesNumbersAndDates()
    {
        var table = TestData.Sales();

        Assert.Equal(3, QueryEngine.Filter(table, [new FilterDefinition { Column = "Кол-во", Operator = FilterOperator.GreaterOrEqual, Value = "3" }]).Count());
        Assert.Equal(3, QueryEngine.Filter(table, [new FilterDefinition { Column = "Дата", Operator = FilterOperator.LessThan, Value = "01.03.2024" }]).Count());
        Assert.Equal(2, QueryEngine.Filter(table, [new FilterDefinition { Column = "Регион", Operator = FilterOperator.In, Values = ["Юг"] }]).Count());
    }

    [Fact]
    public void Filter_InvalidValueForNumericColumn_Throws()
    {
        var table = TestData.Sales();
        Assert.Throws<InvalidOperationException>(() =>
            QueryEngine.Filter(table, [new FilterDefinition { Column = "Цена", Operator = FilterOperator.Equals, Value = "abc" }]).ToList());
    }

    [Fact]
    public void AggregateBy_TextCategory_SortsByValueDescending()
    {
        var table = TestData.Sales();

        var result = QueryEngine.AggregateBy(table, table.Rows.Cast<System.Data.DataRow>(), "Регион", ["Кол-во"], Aggregation.Sum);

        Assert.Equal(["Запад", "Север", "Юг"], result.Categories);
        Assert.Equal([10d, 5d, 1d], result.Series[0]);
    }

    [Fact]
    public void AggregateBy_DateCategory_GroupsByMonthAscending()
    {
        var table = TestData.Sales();

        var result = QueryEngine.AggregateBy(table, table.Rows.Cast<System.Data.DataRow>(), "Дата", ["Цена"], Aggregation.Sum, DateGranularity.Month);

        Assert.Equal(4, result.Categories.Count); // пусто, янв, фев, апр
        Assert.Equal("(пусто)", result.Categories[0]);
        Assert.Equal([70d, 150d, 10d, 40d], result.Series[0]);
    }

    [Fact]
    public void AggregateBy_TopN_KeepsLargest()
    {
        var table = TestData.Sales();

        var result = QueryEngine.AggregateBy(table, table.Rows.Cast<System.Data.DataRow>(), "Регион", ["Цена"], Aggregation.Sum, topN: 1);

        Assert.Equal(["Юг"], result.Categories);
    }

    [Fact]
    public void AggregateBy2D_BuildsRegionByMonthMatrix()
    {
        var table = TestData.Sales();
        var rows = table.Rows.Cast<System.Data.DataRow>();
        var region = ResolvedField.FromColumn(table.Columns["Регион"]!);
        var date = ResolvedField.FromColumn(table.Columns["Дата"]!);
        var qty = ResolvedField.FromColumn(table.Columns["Кол-во"]!);

        var matrix = QueryEngine.AggregateBy2D(rows, region, date, qty, Aggregation.Sum,
            DateGranularity.Month, DateGranularity.Month);

        Assert.Contains("Север", matrix.RowLabels);
        Assert.Contains("Запад", matrix.RowLabels);
        Assert.True(matrix.ColumnLabels.Count >= 2);
        Assert.Equal(matrix.RowLabels.Count, matrix.Cells.Count);
        Assert.All(matrix.Cells, row => Assert.Equal(matrix.ColumnLabels.Count, row.Length));

        var west = matrix.RowLabels.ToList().IndexOf("Запад");
        var apr = matrix.ColumnLabels.ToList().FindIndex(l => l.Contains("апр", StringComparison.OrdinalIgnoreCase)
            || l.Contains("Apr", StringComparison.OrdinalIgnoreCase));
        Assert.True(west >= 0 && apr >= 0);
        Assert.Equal(10d, matrix.Cells[west][apr]);
    }

    [Fact]
    public void GroupBy_CreatesAggregatedTable()
    {
        var table = TestData.Sales();

        var grouped = QueryEngine.GroupBy(table, ["Регион"],
            [new AggregationSpec { Column = "Кол-во", Aggregation = Aggregation.Sum, OutputName = "Штук" },
             new AggregationSpec { Column = "Цена", Aggregation = Aggregation.Count }], "Итоги");

        Assert.Equal("Итоги", grouped.TableName);
        Assert.Equal(3, grouped.Rows.Count);
        var north = grouped.Rows.Cast<System.Data.DataRow>().Single(r => (string)r["Регион"] == "Север");
        Assert.Equal(5d, north["Штук"]);
        Assert.Equal(2L, north["Количество Цена"]);
    }
}
