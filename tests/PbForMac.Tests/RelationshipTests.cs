using System.Data;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.Tests;

public class RelationshipTests
{
    /// <summary>
    /// Звезда как в реальном наборе: продажи → товары → бренды, продажи → точки продаж.
    /// «baseline» содержит ID_PRODUCT с названиями вместо кодов — связь с ним находиться не должна.
    /// </summary>
    private static DataModel StarModel(bool detect = true)
    {
        var sales = new DataTable("sales");
        sales.Columns.Add("ID_PRODUCT", typeof(long));
        sales.Columns.Add("ID_OUTLET", typeof(long));
        sales.Columns.Add("Qty", typeof(long));
        sales.Rows.Add(1L, 10L, 5L);
        sales.Rows.Add(1L, 20L, 3L);
        sales.Rows.Add(2L, 10L, 7L);
        sales.Rows.Add(3L, 20L, 1L);
        sales.Rows.Add(99L, 10L, 4L); // товара нет в справочнике

        var products = new DataTable("products");
        products.Columns.Add("ID_PRODUCT", typeof(long));
        products.Columns.Add("Category", typeof(string));
        products.Columns.Add("ID_BRAND", typeof(long));
        products.Rows.Add(1L, "Chocolate", 100L);
        products.Rows.Add(2L, "Candy", 200L);
        products.Rows.Add(3L, "Chocolate", 200L);

        var brands = new DataTable("brands");
        brands.Columns.Add("ID_BRAND", typeof(long));
        brands.Columns.Add("Country", typeof(string));
        brands.Rows.Add(100L, "Swiss");
        brands.Rows.Add(200L, "Russia");

        var sellers = new DataTable("sellers");
        sellers.Columns.Add("ID_OUTLET", typeof(long));
        sellers.Columns.Add("Chain", typeof(string));
        sellers.Rows.Add(10L, "DailyMart");
        sellers.Rows.Add(20L, "MaxMart");

        var baseline = new DataTable("baseline");
        baseline.Columns.Add("ID_PRODUCT", typeof(string));
        baseline.Rows.Add("Confecta Caramela 40g");
        baseline.Rows.Add("Confecta Royale 45g");

        var model = new DataModel();
        foreach (var table in new[] { sales, products, brands, sellers, baseline })
            model.AddSource(new DataSourceDefinition { TableName = table.TableName }, table);
        if (detect)
            model.DetectRelationships();
        return model;
    }

    private static string Describe(IEnumerable<RelationshipDefinition> relationships) =>
        string.Join("; ", relationships.Select(r => r.ToString()).Order());

    [Fact]
    public void Detect_FindsStarSchemaKeys_AndSkipsFalseMatches()
    {
        var model = StarModel();

        Assert.Equal(
            "products[ID_BRAND] → brands[ID_BRAND]; sales[ID_OUTLET] → sellers[ID_OUTLET]; sales[ID_PRODUCT] → products[ID_PRODUCT]",
            Describe(model.Relationships));
        Assert.Empty(model.RelationshipIssues);
        // Второй запуск ничего не дублирует.
        Assert.Empty(model.DetectRelationships());
    }

    [Fact]
    public void Resolve_FieldsOfRelatedTables_IncludingSecondLevel()
    {
        var model = StarModel();
        var query = new ModelQuery(model);
        var sales = model.GetTable("sales")!;

        var category = query.Resolve(sales, "products[Category]")!;
        var country = query.Resolve(sales, "brands[Country]")!;

        Assert.Equal(["Chocolate", "Chocolate", "Candy", "Chocolate", DBNull.Value], sales.Rows.Cast<DataRow>().Select(category.Get));
        Assert.Equal("Russia", country.Get(sales.Rows[2]));
        Assert.Contains("sellers[Chain]", query.AvailableFields(sales));
        Assert.Contains("brands[Country]", query.AvailableFields(sales));
        // В обратную сторону (от справочника к продажам) поля недоступны.
        Assert.Null(query.Resolve(model.GetTable("products")!, "sales[Qty]"));
        Assert.DoesNotContain(query.AvailableFields(model.GetTable("products")!), f => f.StartsWith("sales["));
    }

    [Fact]
    public void AggregateBy_RelatedCategory()
    {
        var model = StarModel();
        var query = new ModelQuery(model);
        var sales = model.GetTable("sales")!;

        var result = QueryEngine.AggregateBy(sales.Rows.Cast<DataRow>(), query.Resolve(sales, "sellers[Chain]")!,
            [query.Resolve(sales, "Qty")!], Aggregation.Sum);

        Assert.Equal(["DailyMart", "MaxMart"], result.Categories);
        Assert.Equal([16d, 4d], result.Series[0]);
        Assert.Equal("Сумма: Qty", result.SeriesNames[0]);
    }

    [Fact]
    public void Filter_OnDimension_FiltersFactThroughRelationships()
    {
        var model = StarModel();
        var query = new ModelQuery(model);
        var sales = model.GetTable("sales")!;

        // Срез по справочнику товаров (как слайсер на таблице products).
        var byCategory = query.Filter(sales, [new FilterDefinition { Table = "products", Column = "Category", Operator = FilterOperator.In, Values = ["Chocolate"] }]);
        Assert.Equal([5L, 3L, 1L], byCategory.Select(r => r["Qty"]));

        // Фильтр второго уровня: бренды → товары → продажи.
        var byCountry = query.Filter(sales, [new FilterDefinition { Table = "brands", Column = "Country", Operator = FilterOperator.Equals, Value = "Russia" }]);
        Assert.Equal([7L, 1L], byCountry.Select(r => r["Qty"]));

        // Поле связанной таблицы в фильтре самой таблицы продаж.
        var byChain = query.Filter(sales, [new FilterDefinition { Table = "sales", Column = "sellers[Chain]", Operator = FilterOperator.In, Values = ["MaxMart"] }]);
        Assert.Equal([3L, 1L], byChain.Select(r => r["Qty"]));
    }

    [Fact]
    public void Filter_OnFact_DoesNotFilterDimension_AndUnrelatedFiltersAreIgnored()
    {
        var model = StarModel();
        var query = new ModelQuery(model);

        var products = query.Filter(model.GetTable("products")!,
        [
            new FilterDefinition { Table = "sales", Column = "Qty", Operator = FilterOperator.GreaterThan, Value = "100" },
            new FilterDefinition { Table = "baseline", Column = "ID_PRODUCT", Operator = FilterOperator.Equals, Value = "x" },
        ]);

        Assert.Equal(3, products.Count());
    }

    [Fact]
    public void Relationships_AreValidated_RemovedWithTable_AndSaved()
    {
        var model = StarModel();

        Assert.Throws<InvalidOperationException>(() => model.AddRelationship(new RelationshipDefinition
            { FromTable = "sales", FromColumn = "Qty", ToTable = "products", ToColumn = "ID_PRODUCT" }));
        Assert.Throws<InvalidOperationException>(() => model.AddRelationship(new RelationshipDefinition
            { FromTable = "sales", FromColumn = "Nope", ToTable = "baseline", ToColumn = "ID_PRODUCT" }));

        // Переименование ключа ломает связь — это видно в проблемах связи.
        model.AddStep(new RenameColumnStep { Table = "sellers", Column = "ID_OUTLET", NewName = "Outlet" });
        var broken = model.Relationships.Single(r => r.ToTable == "sellers");
        Assert.Contains("не найден", model.RelationshipIssues[broken]);
        Assert.Null(new ModelQuery(model).Resolve(model.GetTable("sales")!, "sellers[Chain]"));

        var json = ReportSerializer.Serialize(new ReportDefinition { Relationships = model.Relationships });
        Assert.Equal(3, ReportSerializer.Deserialize(json).Relationships.Count);

        model.RemoveTable("products");
        Assert.Equal("sales[ID_OUTLET] → sellers[ID_OUTLET]", Describe(model.Relationships));
    }

    [Fact]
    public void Detect_OneToOne_PointsFromBiggerTable()
    {
        var price = new DataTable("price");
        price.Columns.Add("ID_PRODUCT", typeof(long));
        price.Columns.Add("Price", typeof(double));
        for (var i = 1L; i <= 4; i++)
            price.Rows.Add(i, i * 1.5);
        var products = new DataTable("products");
        products.Columns.Add("ID_PRODUCT", typeof(long));
        for (var i = 1L; i <= 3; i++)
            products.Rows.Add(i);

        var model = new DataModel();
        model.AddSource(new DataSourceDefinition { TableName = "products" }, products);
        model.AddSource(new DataSourceDefinition { TableName = "price" }, price);
        model.DetectRelationships();

        // Все ключи products есть в price (100 %), а ключи price в products — только на 75 %,
        // поэтому связь идёт products → price: из продаж цены доступны через товары.
        Assert.Equal("products[ID_PRODUCT] → price[ID_PRODUCT]", Describe(model.Relationships));
    }

    [Fact]
    public void DiagramLayout_PlacesLookupsToTheRightOfFacts()
    {
        var model = StarModel();
        var positions = DiagramLayout.Arrange(model.Tables, model.Relationships);

        Assert.True(positions["sales"].X < positions["products"].X);
        Assert.True(positions["products"].X < positions["brands"].X);
        Assert.True(positions["sales"].X < positions["sellers"].X);
    }

    [Fact]
    public void ReportSerializer_RoundTripsTableLayouts()
    {
        var report = new ReportDefinition
        {
            Relationships =
            [
                new RelationshipDefinition { FromTable = "a", FromColumn = "id", ToTable = "b", ToColumn = "id" },
            ],
            TableLayouts =
            [
                new TableLayoutDefinition { Table = "a", X = 40, Y = 60 },
                new TableLayoutDefinition { Table = "b", X = 300, Y = 60 },
            ],
        };

        var restored = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));
        Assert.Equal(2, restored.TableLayouts.Count);
        Assert.Equal(40, restored.TableLayouts[0].X);
        Assert.Equal("b", restored.TableLayouts[1].Table);
    }

    [Fact]
    public void DataModel_AutoLayoutPersistsPositions()
    {
        var model = StarModel();
        model.AutoLayoutTables();
        Assert.Equal(model.Tables.Count, model.TableLayouts.Count);
        Assert.NotNull(model.GetTableLayout("sales"));
        Assert.True(model.GetTableLayout("sales")!.X < model.GetTableLayout("products")!.X);
    }
}
