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
    public void RemoveKeepAndMoveColumns()
    {
        var tables = Tables();
        var table = tables[0];

        TransformEngine.Apply(tables, new MoveColumnStep { Table = "Продажи", Column = "Цена", NewOrdinal = 0 });
        Assert.Equal(0, table.Columns["Цена"]!.Ordinal);

        TransformEngine.Apply(tables, new KeepColumnsStep { Table = "Продажи", Columns = ["Цена", "Кол-во", "Регион"] });
        Assert.Equal(3, table.Columns.Count);
        Assert.Equal(0, table.Columns["Цена"]!.Ordinal);
        Assert.Equal(1, table.Columns["Кол-во"]!.Ordinal);

        TransformEngine.Apply(tables, new RemoveColumnsStep { Table = "Продажи", Columns = ["Цена", "Кол-во"] });
        Assert.Equal(["Регион"], table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray());
    }

    [Fact]
    public void RemoveColumns_RejectsRemovingAll()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            TransformEngine.Apply(Tables(), new RemoveColumnsStep
            {
                Table = "Продажи",
                Columns = Tables()[0].Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList(),
            }));
        Assert.Contains("все столбцы", error.Message);
    }

    [Fact]
    public void RemoveAndKeepRows_ByFingerprint()
    {
        var tables = Tables();
        var table = tables[0];
        var first = TransformEngine.RowKey(table.Rows[0]);
        var second = TransformEngine.RowKey(table.Rows[1]);

        TransformEngine.Apply(tables, new RemoveRowsStep { Table = "Продажи", RowKeys = [first] });
        Assert.DoesNotContain(table.Rows.Cast<DataRow>(), r => TransformEngine.RowKey(r) == first);

        var before = table.Rows.Count;
        TransformEngine.Apply(tables, new KeepRowsStep { Table = "Продажи", RowKeys = [second] });
        Assert.Equal(1, table.Rows.Count);
        Assert.Equal(second, TransformEngine.RowKey(table.Rows[0]));
        Assert.True(before > 1);
    }

    [Fact]
    public void ReplaceValues_EntireCellAndSubstring()
    {
        var tables = Tables();
        var table = tables[0];

        TransformEngine.Apply(tables, new ReplaceValuesStep
        {
            Table = "Продажи",
            Column = "Регион",
            Find = "Юг",
            Replace = "Южный",
            MatchEntireCell = true,
        });
        Assert.Equal(2, table.Rows.Cast<DataRow>().Count(r => Equals(r["Регион"], "Южный")));
        Assert.DoesNotContain(table.Rows.Cast<DataRow>(), r => Equals(r["Регион"], "Юг"));

        TransformEngine.Apply(tables, new ReplaceValuesStep
        {
            Table = "Продажи",
            Column = "Регион",
            Find = "Юж",
            Replace = "Юг",
            MatchEntireCell = false,
        });
        Assert.Contains(table.Rows.Cast<DataRow>(), r => Equals(r["Регион"], "Югный"));
    }

    [Fact]
    public void FillDownAndFillUp()
    {
        var table = new DataTable("T");
        table.Columns.Add("A", typeof(string));
        table.Rows.Add("x");
        table.Rows.Add(DBNull.Value);
        table.Rows.Add(DBNull.Value);
        table.Rows.Add("y");
        table.Rows.Add(DBNull.Value);
        var tables = new List<DataTable> { table };

        TransformEngine.Apply(tables, new FillDownStep { Table = "T", Column = "A" });
        Assert.Equal(["x", "x", "x", "y", "y"], table.Rows.Cast<DataRow>().Select(r => (string)r["A"]).ToArray());

        table.Rows[1]["A"] = DBNull.Value;
        table.Rows[2]["A"] = DBNull.Value;
        TransformEngine.Apply(tables, new FillUpStep { Table = "T", Column = "A" });
        Assert.Equal("y", table.Rows[1]["A"]);
        Assert.Equal("y", table.Rows[2]["A"]);
    }

    [Fact]
    public void RemoveBlankRows_AllColumnsAndSelected()
    {
        var table = new DataTable("T");
        table.Columns.Add("A", typeof(string));
        table.Columns.Add("B", typeof(string));
        table.Rows.Add("a", "b");
        table.Rows.Add(DBNull.Value, DBNull.Value);
        table.Rows.Add("", "  ");
        table.Rows.Add("c", DBNull.Value);
        var tables = new List<DataTable> { table };

        TransformEngine.Apply(tables, new RemoveBlankRowsStep { Table = "T" });
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("a", table.Rows[0]["A"]);
        Assert.Equal("c", table.Rows[1]["A"]);

        TransformEngine.Apply(tables, new RemoveBlankRowsStep { Table = "T", Columns = ["B"] });
        Assert.Single(table.Rows);
        Assert.Equal("a", table.Rows[0]["A"]);
    }

    [Fact]
    public void SplitColumn_ByDelimiter()
    {
        var table = new DataTable("T");
        table.Columns.Add("Код", typeof(string));
        table.Columns.Add("Другое", typeof(int));
        table.Rows.Add("A-B-C", 1);
        table.Rows.Add("X-Y", 2);
        table.Rows.Add(DBNull.Value, 3);
        var tables = new List<DataTable> { table };

        TransformEngine.Apply(tables, new SplitColumnStep { Table = "T", Column = "Код", Delimiter = "-" });

        Assert.False(table.Columns.Contains("Код"));
        Assert.True(table.Columns.Contains("Код.1"));
        Assert.True(table.Columns.Contains("Код.2"));
        Assert.True(table.Columns.Contains("Код.3"));
        Assert.Equal(0, table.Columns["Код.1"]!.Ordinal);
        Assert.Equal("A", table.Rows[0]["Код.1"]);
        Assert.Equal("B", table.Rows[0]["Код.2"]);
        Assert.Equal("C", table.Rows[0]["Код.3"]);
        Assert.Equal("X", table.Rows[1]["Код.1"]);
        Assert.Equal("Y", table.Rows[1]["Код.2"]);
        Assert.True(TypeInference.IsEmpty(table.Rows[1]["Код.3"]));
        Assert.True(TypeInference.IsEmpty(table.Rows[2]["Код.1"]));
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
                new RemoveColumnsStep { Table = "A", Columns = ["a", "b"] },
                new KeepColumnsStep { Table = "A", Columns = ["x", "S"] },
                new MoveColumnStep { Table = "A", Column = "x", NewOrdinal = 0 },
                new RemoveRowsStep { Table = "A", RowKeys = ["k1", "k2"] },
                new KeepRowsStep { Table = "A", RowKeys = ["k1"] },
                new ReplaceValuesStep { Table = "A", Column = "x", Find = "1", Replace = "2" },
                new FillDownStep { Table = "A", Column = "x" },
                new FillUpStep { Table = "A", Column = "x" },
                new RemoveBlankRowsStep { Table = "A", Columns = ["x"] },
                new SplitColumnStep { Table = "A", Column = "x", Delimiter = ";", MaxParts = 3 },
                new SortRowsStep { Table = "A", Column = "x", Descending = true },
            ],
            Visuals = [new VisualDefinition { Kind = VisualKind.Pie, Table = "A", ValueFields = ["x"] }],
        };

        var json = ReportSerializer.Serialize(report);
        var restored = ReportSerializer.Deserialize(json);

        Assert.Contains("\"calculated\"", json);
        Assert.Contains("\"removeColumns\"", json);
        Assert.Contains("\"keepColumns\"", json);
        Assert.Contains("\"moveColumn\"", json);
        Assert.Contains("\"removeRows\"", json);
        Assert.Contains("\"keepRows\"", json);
        Assert.Contains("\"replaceValues\"", json);
        Assert.Contains("\"fillDown\"", json);
        Assert.Contains("\"fillUp\"", json);
        Assert.Contains("\"removeBlankRows\"", json);
        Assert.Contains("\"splitColumn\"", json);
        Assert.Contains("\"sortRows\"", json);
        Assert.IsType<CalculatedColumnStep>(restored.Steps[0]);
        Assert.Equal(["1"], ((FilterRowsStep)restored.Steps[1]).Filter.Values);
        Assert.Equal(["a", "b"], ((RemoveColumnsStep)restored.Steps[2]).Columns);
        Assert.Equal(["x", "S"], ((KeepColumnsStep)restored.Steps[3]).Columns);
        Assert.Equal(0, ((MoveColumnStep)restored.Steps[4]).NewOrdinal);
        Assert.Equal(["k1", "k2"], ((RemoveRowsStep)restored.Steps[5]).RowKeys);
        Assert.Equal(["k1"], ((KeepRowsStep)restored.Steps[6]).RowKeys);
        Assert.Equal("2", ((ReplaceValuesStep)restored.Steps[7]).Replace);
        Assert.Equal("x", ((FillDownStep)restored.Steps[8]).Column);
        Assert.Equal("x", ((FillUpStep)restored.Steps[9]).Column);
        Assert.Equal(["x"], ((RemoveBlankRowsStep)restored.Steps[10]).Columns);
        Assert.Equal(";", ((SplitColumnStep)restored.Steps[11]).Delimiter);
        Assert.True(((SortRowsStep)restored.Steps[12]).Descending);
        Assert.Equal(VisualKind.Pie, restored.Visuals[0].Kind);
    }

    [Fact]
    public void TextTransform_TrimAndCase()
    {
        var table = new DataTable("T");
        table.Columns.Add("A", typeof(string));
        table.Rows.Add("  Hi ");
        table.Rows.Add("x");
        var tables = new List<DataTable> { table };

        TransformEngine.Apply(tables, new TextTransformStep { Table = "T", Column = "A", Kind = TextTransformKind.Trim });
        Assert.Equal("Hi", tables[0].Rows[0]["A"]);

        TransformEngine.Apply(tables, new TextTransformStep { Table = "T", Column = "A", Kind = TextTransformKind.Upper });
        Assert.Equal("HI", tables[0].Rows[0]["A"]);
        Assert.Equal("X", tables[0].Rows[1]["A"]);
    }

    [Fact]
    public void AppendTable_InPlaceAndNewTable()
    {
        var a = new DataTable("A");
        a.Columns.Add("x", typeof(string));
        a.Rows.Add("1");
        var b = new DataTable("B");
        b.Columns.Add("x", typeof(string));
        b.Columns.Add("y", typeof(string));
        b.Rows.Add("2", "b");
        var tables = new List<DataTable> { a, b };

        TransformEngine.Apply(tables, new AppendTableStep { Table = "A", OtherTable = "B" });
        Assert.Equal(2, tables[0].Rows.Count);
        Assert.True(tables[0].Columns.Contains("y"));
        Assert.Equal("2", tables[0].Rows[1]["x"]);
        Assert.Equal("b", tables[0].Rows[1]["y"]);

        TransformEngine.Apply(tables, new AppendTableStep { Table = "A", OtherTable = "B", NewTable = "C" });
        Assert.Equal(3, tables.Count);
        Assert.Equal("C", tables[2].TableName);
        Assert.Equal(3, tables[2].Rows.Count); // клон A (2) + B (1)
    }

    [Fact]
    public void UnpivotColumns_CreatesAttributeValueRows()
    {
        var table = new DataTable("T");
        table.Columns.Add("Id", typeof(string));
        table.Columns.Add("Янв", typeof(int));
        table.Columns.Add("Фев", typeof(int));
        table.Rows.Add("a", 10, 20);
        table.Rows.Add("b", 1, 2);
        var tables = new List<DataTable> { table };

        TransformEngine.Apply(tables, new UnpivotColumnsStep
        {
            Table = "T",
            Columns = ["Янв", "Фев"],
        });

        var result = tables[0];
        Assert.Equal(["Id", "Атрибут", "Значение"], result.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray());
        Assert.Equal(4, result.Rows.Count);
        Assert.Contains(result.Rows.Cast<DataRow>(), r => Equals(r["Id"], "a") && Equals(r["Атрибут"], "Янв") && Equals(Convert.ToInt32(r["Значение"]), 10));
        Assert.Contains(result.Rows.Cast<DataRow>(), r => Equals(r["Id"], "b") && Equals(r["Атрибут"], "Фев") && Equals(Convert.ToInt32(r["Значение"]), 2));
    }

    [Fact]
    public void SortRows_OrdersAscendingAndDescending()
    {
        var tables = Tables();
        TransformEngine.Apply(tables, new SortRowsStep { Table = "Продажи", Column = "Регион", Descending = false });
        var ascending = tables[0].Rows.Cast<DataRow>().Select(r => (string)r["Регион"]).ToList();
        Assert.Equal(ascending.OrderBy(x => x, StringComparer.Create(new System.Globalization.CultureInfo("ru-RU"), ignoreCase: true)).ToList(), ascending);

        TransformEngine.Apply(tables, new SortRowsStep { Table = "Продажи", Column = "Регион", Descending = true });
        var descending = tables[0].Rows.Cast<DataRow>().Select(r => (string)r["Регион"]).ToList();
        Assert.Equal(ascending.AsEnumerable().Reverse().ToList(), descending);
    }

    [Fact]
    public void DataModel_MoveStep_ReordersAndRebuilds()
    {
        var model = new DataModel();
        model.AddSource(new DataSourceDefinition { TableName = "Продажи" }, TestData.Sales());
        model.AddStep(new RenameColumnStep { Table = "Продажи", Column = "Регион", NewName = "Область" });
        model.AddStep(new SortRowsStep { Table = "Продажи", Column = "Область", Descending = false });

        Assert.True(model.MoveStep(model.Steps[1], -1));
        Assert.IsType<SortRowsStep>(model.Steps[0]);
        Assert.IsType<RenameColumnStep>(model.Steps[1]);
        // После перестановки сортировка идёт до переименования — столбца «Область» ещё нет.
        Assert.True(model.StepErrors.ContainsKey(model.Steps[0]));
        Assert.True(model.GetTable("Продажи")!.Columns.Contains("Область"));
    }

    [Fact]
    public void DataModel_SkipsDisabledSteps()
    {
        var model = new DataModel();
        model.AddSource(new DataSourceDefinition { TableName = "Продажи" }, TestData.Sales());
        model.AddStep(new RenameColumnStep { Table = "Продажи", Column = "Регион", NewName = "Область" });
        model.AddStep(new RemoveColumnStep { Table = "Продажи", Column = "Цена", Enabled = false });

        var table = model.GetTable("Продажи")!;
        Assert.True(table.Columns.Contains("Область"));
        Assert.False(table.Columns.Contains("Регион"));
        Assert.True(table.Columns.Contains("Цена"));

        model.Steps[1].Enabled = true;
        model.Rebuild();
        Assert.False(model.GetTable("Продажи")!.Columns.Contains("Цена"));
    }

    [Fact]
    public void ReportSerializer_RoundTripsEnabledAndFormatting()
    {
        var report = new ReportDefinition
        {
            Sources = [new DataSourceDefinition { Kind = SourceKind.Csv, Path = "/tmp/a.csv", TableName = "A" }],
            Steps = [new RemoveColumnStep { Table = "A", Column = "x", Enabled = false }],
            Visuals =
            [
                new VisualDefinition
                {
                    Kind = VisualKind.Column,
                    Table = "A",
                    ShowLegend = false,
                    ShowDataLabels = true,
                },
            ],
        };

        var restored = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));
        Assert.False(restored.Steps[0].Enabled);
        Assert.False(restored.Visuals[0].ShowLegend);
        Assert.True(restored.Visuals[0].ShowDataLabels);
    }
}
