using System.Data;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.Tests;

public class MeasureTests
{
    private static DataModel ConfectaModel()
    {
        var sales = new DataTable("Sales");
        sales.Columns.Add("ProductId", typeof(long));
        sales.Columns.Add("Revenue", typeof(double));
        sales.Rows.Add(1L, 100d);
        sales.Rows.Add(1L, 50d);
        sales.Rows.Add(2L, 80d);
        sales.Rows.Add(3L, 20d);

        var products = new DataTable("Products");
        products.Columns.Add("ProductId", typeof(long));
        products.Columns.Add("Brand", typeof(string));
        products.Rows.Add(1L, "Confecta");
        products.Rows.Add(2L, "Other");
        products.Rows.Add(3L, "Confecta");

        var model = new DataModel();
        model.AddSource(new DataSourceDefinition { TableName = "Sales" }, sales);
        model.AddSource(new DataSourceDefinition { TableName = "Products" }, products);
        model.AddRelationship(new RelationshipDefinition
        {
            FromTable = "Sales", FromColumn = "ProductId",
            ToTable = "Products", ToColumn = "ProductId",
        });
        return model;
    }

    private static MeasureEngine EngineWithBaseMeasures(DataModel model)
    {
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Total Revenue",
            Table = "Sales",
            Expression = "SUM([Revenue])",
        });
        return new MeasureEngine(model, new ModelQuery(model));
    }

    [Theory]
    [InlineData("SUM([Revenue])")]
    [InlineData("SUM(Sales[Revenue])")]
    [InlineData("sum( revenue )")]
    public void Parser_AcceptsSum(string expression)
    {
        var expr = MeasureParser.Parse(expression);
        Assert.IsType<SumColumnExpr>(expr);
    }

    [Fact]
    public void Parser_ParsesCalculateKeepFilters()
    {
        var expr = MeasureParser.Parse(
            """CALCULATE([Total Revenue], KEEPFILTERS(Products[Brand] = "Confecta"))""");
        var calc = Assert.IsType<CalculateExpr>(expr);
        var reference = Assert.IsType<MeasureRefExpr>(calc.Inner);
        Assert.Equal("Total Revenue", reference.MeasureName);
        var keep = Assert.IsType<FilterEqualsModifier>(Assert.Single(calc.Modifiers));
        Assert.True(keep.KeepExisting);
        Assert.Equal("Products", keep.Table);
        Assert.Equal("Brand", keep.Column);
        Assert.Equal("Confecta", keep.Value);
    }

    [Fact]
    public void Parser_ParsesArithmeticDivideAndAll()
    {
        var binary = Assert.IsType<BinaryExpr>(MeasureParser.Parse("[A] + [B] * 2"));
        Assert.Equal(MeasureBinaryOp.Add, binary.Op);
        Assert.IsType<BinaryExpr>(binary.Right);

        var divide = Assert.IsType<DivideFnExpr>(MeasureParser.Parse("DIVIDE([A], [B], 0)"));
        Assert.IsType<NumberExpr>(divide.Alternate);

        var calc = Assert.IsType<CalculateExpr>(
            MeasureParser.Parse("CALCULATE([Total Revenue], ALL(Products), Products[Brand] = \"Confecta\")"));
        Assert.Equal(2, calc.Modifiers.Count);
        Assert.IsType<RemoveFiltersModifier>(calc.Modifiers[0]);
        var replace = Assert.IsType<FilterEqualsModifier>(calc.Modifiers[1]);
        Assert.False(replace.KeepExisting);
    }

    [Fact]
    public void Engine_SumAndCalculateKeepFilters()
    {
        var model = ConfectaModel();
        var engine = EngineWithBaseMeasures(model);
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Rev Brand Confecta",
            Table = "Sales",
            Expression = """CALCULATE([Total Revenue], KEEPFILTERS(Products[Brand] = "Confecta"))""",
        });

        Assert.Equal(250d, engine.Evaluate("Total Revenue", []));
        Assert.Equal(170d, engine.Evaluate("Rev Brand Confecta", []));
    }

    [Fact]
    public void Engine_KeepFilters_IntersectsOuterFilter()
    {
        var model = ConfectaModel();
        var engine = EngineWithBaseMeasures(model);
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Rev Brand Confecta",
            Table = "Sales",
            Expression = """CALCULATE([Total Revenue], KEEPFILTERS(Products[Brand] = "Confecta"))""",
        });

        var filtered = engine.Evaluate("Rev Brand Confecta",
        [
            new FilterDefinition
            {
                Table = "Products", Column = "Brand", Operator = FilterOperator.Equals, Value = "Other",
            },
        ]);
        Assert.True(double.IsNaN(filtered) || filtered == 0d);
    }

    [Fact]
    public void Engine_ReplaceFilter_OverridesOuter()
    {
        var model = ConfectaModel();
        var engine = EngineWithBaseMeasures(model);
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Rev Confecta Replace",
            Table = "Sales",
            Expression = """CALCULATE([Total Revenue], Products[Brand] = "Confecta")""",
        });

        // Внешний Other заменяется на Confecta → 170
        var value = engine.Evaluate("Rev Confecta Replace",
        [
            new FilterDefinition
            {
                Table = "Products", Column = "Brand", Operator = FilterOperator.Equals, Value = "Other",
            },
        ]);
        Assert.Equal(170d, value);
    }

    [Fact]
    public void Engine_All_ClearsBrandFilter()
    {
        var model = ConfectaModel();
        var engine = EngineWithBaseMeasures(model);
        model.AddMeasure(new MeasureDefinition
        {
            Name = "All Revenue",
            Table = "Sales",
            Expression = "CALCULATE([Total Revenue], ALL(Products))",
        });

        var value = engine.Evaluate("All Revenue",
        [
            new FilterDefinition
            {
                Table = "Products", Column = "Brand", Operator = FilterOperator.Equals, Value = "Other",
            },
        ]);
        Assert.Equal(250d, value);
    }

    [Fact]
    public void Engine_DivideAndPercentOfTotal()
    {
        var model = ConfectaModel();
        var engine = EngineWithBaseMeasures(model);
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Pct of All",
            Table = "Sales",
            Expression = "DIVIDE([Total Revenue], CALCULATE([Total Revenue], ALL(Products)), 0)",
        });

        var pct = engine.Evaluate("Pct of All",
        [
            new FilterDefinition
            {
                Table = "Products", Column = "Brand", Operator = FilterOperator.Equals, Value = "Confecta",
            },
        ]);
        Assert.Equal(170d / 250d, pct, 6);
    }

    [Fact]
    public void Engine_CountRowsAndArithmetic()
    {
        var model = ConfectaModel();
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Rows",
            Table = "Sales",
            Expression = "COUNTROWS()",
        });
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Scaled",
            Table = "Sales",
            Expression = "SUM([Revenue]) * 2 + 10",
        });
        var engine = new MeasureEngine(model, new ModelQuery(model));
        Assert.Equal(4d, engine.Evaluate("Rows", []));
        Assert.Equal(510d, engine.Evaluate("Scaled", []));
    }

    [Fact]
    public void ReportSerializer_RoundTripsMeasures()
    {
        var report = new ReportDefinition
        {
            Measures =
            [
                new MeasureDefinition
                {
                    Name = "Total Revenue",
                    Table = "Sales",
                    Expression = "SUM([Revenue])",
                },
            ],
        };
        var restored = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));
        Assert.Single(restored.Measures);
        Assert.Equal("Total Revenue", restored.Measures[0].Name);
        Assert.Equal("SUM([Revenue])", restored.Measures[0].Expression);
    }
}
