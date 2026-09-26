using PbForMac.Services;

namespace PbForMac.Tests;

/// <summary>Демо-отчёт из samples/ должен открываться без ошибок: все источники и шаги.</summary>
public class SampleReportTests
{
    private static string SamplesDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var samples = Path.Combine(dir.FullName, "samples");
            if (File.Exists(Path.Combine(samples, "demo.pbm")))
                return samples;
        }
        throw new DirectoryNotFoundException("Папка samples не найдена.");
    }

    [Fact]
    public void DemoReport_LoadsAllSourcesAndSteps()
    {
        var report = ReportSerializer.Load(Path.Combine(SamplesDirectory(), "demo.pbm"));
        var model = new DataModel();

        var errors = model.Load(report.Sources, report.Steps);

        Assert.Empty(errors);
        Assert.Empty(model.StepErrors);
        Assert.Equal(6, model.Tables.Count);

        var sales = model.GetTable("Продажи")!;
        Assert.Equal(typeof(DateTime), sales.Columns["Дата"]!.DataType);
        Assert.Equal(typeof(double), sales.Columns["Цена"]!.DataType);
        Assert.Equal(typeof(double), sales.Columns["Выручка"]!.DataType);

        // Все поля, на которые ссылаются визуалы, существуют.
        foreach (var visual in report.Visuals)
        {
            var table = model.GetTable(visual.Table);
            Assert.NotNull(table);
            foreach (var field in visual.ValueFields.Append(visual.CategoryField).OfType<string>())
                Assert.True(table.Columns.Contains(field), $"{visual.Title}: нет поля {field}");
        }
    }
}
