using System.Data;
using System.IO.Compression;
using System.Text;
using PbForMac.Models;
using PbForMac.Services;

namespace PbForMac.Tests;

public class PbipExchangeTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsModelAndVisualsWithoutSidecar()
    {
        using var temp = new TempFiles();
        var sales = new DataTable("Sales");
        sales.Columns.Add("ProductId", typeof(long));
        sales.Columns.Add("Revenue", typeof(double));
        sales.Rows.Add(1L, 100d);
        sales.Rows.Add(2L, 50d);
        var products = new DataTable("Products");
        products.Columns.Add("ProductId", typeof(long));
        products.Columns.Add("Brand", typeof(string));
        products.Rows.Add(1L, "Confecta");
        products.Rows.Add(2L, "Other");

        var report = new ReportDefinition
        {
            Measures =
            [
                new MeasureDefinition
                {
                    Name = "Total Revenue",
                    Table = "Sales",
                    Expression = "SUM(Sales[Revenue])",
                },
            ],
            Relationships =
            [
                new RelationshipDefinition
                {
                    FromTable = "Sales", FromColumn = "ProductId",
                    ToTable = "Products", ToColumn = "ProductId",
                    FilterDirection = FilterDirection.Both,
                },
            ],
            Pages =
            [
                new ReportPage
                {
                    Name = "Обзор",
                    CanvasWidth = 1280,
                    CanvasHeight = 720,
                    Visuals =
                    [
                        new VisualDefinition
                        {
                            Kind = VisualKind.Column,
                            Table = "Sales",
                            CategoryField = "Products[Brand]",
                            ValueFields = [MeasureRef.Format("Total Revenue")],
                            X = 20,
                            Y = 30,
                            Width = 400,
                            Height = 240,
                            Title = "Выручка",
                        },
                        new VisualDefinition
                        {
                            Kind = VisualKind.Text,
                            Text = "Заголовок",
                            X = 20,
                            Y = 0,
                            Width = 200,
                            Height = 40,
                            ShowTitle = false,
                        },
                    ],
                },
            ],
        };

        var pbip = temp.PathOf("Shop.pbip");
        PbipExchange.Save(report, [sales, products], pbip);
        File.Delete(temp.PathOf("Shop" + PbipExchange.SidecarSuffix));

        var loaded = PbipExchange.Load(pbip, out var warning);
        Assert.NotNull(warning);
        Assert.Equal(2, loaded.Sources.Count);
        Assert.All(loaded.Sources, s => Assert.True(File.Exists(s.Path)));
        Assert.Equal("Total Revenue", loaded.Measures[0].Name);
        Assert.Contains("SUM(Sales[Revenue])", loaded.Measures[0].Expression);
        Assert.Equal("Products", loaded.Relationships[0].ToTable);
        Assert.Equal(FilterDirection.Both, loaded.Relationships[0].FilterDirection);

        var model = new DataModel();
        var errors = model.Load(loaded.Sources, [], loaded.Relationships, [], loaded.Measures);
        Assert.Empty(errors);
        Assert.Equal(150d, new MeasureEngine(model, new ModelQuery(model)).Evaluate("Total Revenue", []));

        var column = loaded.Pages[0].Visuals.Single(v => v.Kind == VisualKind.Column);
        Assert.Equal("Products[Brand]", column.CategoryField);
        Assert.Equal(MeasureRef.Format("Total Revenue"), column.ValueFields[0]);
        Assert.Equal("Выручка", column.Title);
        Assert.Contains(loaded.Pages[0].Visuals, v => v.Kind == VisualKind.Text && v.Text == "Заголовок");

        var withSidecar = PbipExchange.Save(report, [sales, products], pbip);
        Assert.Contains(".pbip", withSidecar);
        var native = PbipExchange.Load(pbip, out var noWarning);
        Assert.Null(noWarning);
        Assert.Equal("Обзор", native.Pages[0].Name);
    }

    [Fact]
    public void Pbix_ReadsLayoutPagesAndVisuals()
    {
        using var temp = new TempFiles();
        var path = temp.PathOf("report.pbix");
        var layout = """
            {
              "sections": [
                {
                  "displayName": "Страница 1",
                  "width": 1280,
                  "height": 720,
                  "visualContainers": [
                    {
                      "x": 10, "y": 20, "width": 300, "height": 200,
                      "config": "{\"singleVisual\":{\"visualType\":\"clusteredColumnChart\",\"projections\":{\"Category\":[{\"queryRef\":\"Sales.Brand\"}],\"Y\":[{\"queryRef\":\"Sum(Sales.Revenue)\"}]}}}"
                    }
                  ]
                }
              ]
            }
            """;
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("Report/Layout");
            using var writer = new StreamWriter(entry.Open(), new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
            writer.Write(layout);
        }

        var report = PbixExchange.Load(path, out var warning);
        Assert.Contains("VertiPaq", warning);
        Assert.Equal("Страница 1", report.Pages[0].Name);
        var visual = Assert.Single(report.Pages[0].Visuals);
        Assert.Equal(VisualKind.Column, visual.Kind);
        Assert.Equal("Sales", visual.Table);
        Assert.Equal("Sales[Brand]", visual.CategoryField);
        Assert.Equal("Sales[Revenue]", visual.ValueFields[0]);
        Assert.Equal(10, visual.X);
    }
}
