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
        Assert.Single(calc.KeepFilters);
        Assert.Equal("Products", calc.KeepFilters[0].Table);
        Assert.Equal("Brand", calc.KeepFilters[0].Column);
        Assert.Equal("Confecta", calc.KeepFilters[0].Value);
    }

    [Fact]
    public void Engine_SumAndCalculateKeepFilters()
    {
        var model = ConfectaModel();
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Total Revenue",
            Table = "Sales",
            Expression = "SUM([Revenue])",
        });
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Rev Brand Confecta",
            Table = "Sales",
            Expression = """CALCULATE([Total Revenue], KEEPFILTERS(Products[Brand] = "Confecta"))""",
        });

        var query = new ModelQuery(model);
        var engine = new MeasureEngine(model, query);

        Assert.Equal(250d, engine.Evaluate("Total Revenue", []));
        // Confecta: 100+50+20 = 170
        Assert.Equal(170d, engine.Evaluate("Rev Brand Confecta", []));
    }

    [Fact]
    public void Engine_KeepFilters_IntersectsOuterFilter()
    {
        var model = ConfectaModel();
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Total Revenue",
            Table = "Sales",
            Expression = "SUM(Sales[Revenue])",
        });
        model.AddMeasure(new MeasureDefinition
        {
            Name = "Rev Brand Confecta",
            Table = "Sales",
            Expression = """CALCULATE([Total Revenue], KEEPFILTERS(Products[Brand] = "Confecta"))""",
        });

        var query = new ModelQuery(model);
        var engine = new MeasureEngine(model, query);
        // Outer filter Other ∩ Confecta → empty
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
