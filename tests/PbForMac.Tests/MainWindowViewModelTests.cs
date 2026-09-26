using PbForMac.Services;
using PbForMac.Services.Importers;
using PbForMac.ViewModels;

namespace PbForMac.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly TempFiles _files = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly FakeTheme _theme = new();

    public void Dispose() => _files.Dispose();

    private MainWindowViewModel CreateViewModel() =>
        new(_dialogs, _theme, new AppSettings { FilePath = _files.PathOf("settings.json") });

    private string CreatePartsFolder()
    {
        var folder = Path.Combine(_files.Directory, "sales_parts");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "sales_2024_01.csv"), "Дата;Сумма\n05.01.2024;100\n");
        File.WriteAllText(Path.Combine(folder, "sales_2024_02.csv"), "Дата;Сумма\n03.02.2024;200\n07.02.2024;50\n");
        return folder;
    }

    [Fact]
    public async Task Folder_Combine_CreatesSingleFolderSource()
    {
        var folder = CreatePartsFolder();
        _dialogs.Choice = 0;
        var vm = CreateViewModel();

        await vm.OpenPathAsync(folder);

        var source = Assert.Single(vm.Model.Sources);
        Assert.Equal(Models.SourceKind.Folder, source.Kind);
        var table = vm.Model.GetTable("sales_parts")!;
        Assert.Equal(3, table.Rows.Count);
        Assert.True(table.Columns.Contains(FolderImporter.FileColumn));
        Assert.Contains("Объединить", _dialogs.LastOptions![0]);
        Assert.Same(vm.Data, vm.CurrentPage);
    }

    /// <summary>Структура как у типичного набора: справочники в корне и помесячные части в подпапке.</summary>
    private string CreateDatasetFolder()
    {
        var root = Path.Combine(_files.Directory, "dataset");
        var parts = Path.Combine(root, "sales", "sales_v2_parts");
        Directory.CreateDirectory(parts);
        File.WriteAllText(Path.Combine(root, "price.csv"), "ID_PRODUCT,Price\n1,1.5\n");
        File.WriteAllText(Path.Combine(root, "sellers.csv"), "ID_OUTLET,Retail Chain\n1,DailyMart\n");
        File.WriteAllText(Path.Combine(parts, "sales_2024_01.csv"), "year,month,Sales\n2024,1,10\n");
        File.WriteAllText(Path.Combine(parts, "sales_2024_02.csv"), "year,month,Sales\n2024,2,20\n2024,2,5\n");
        return root;
    }

    [Fact]
    public async Task FolderWithSubfolders_BySubfolders_CombinesEachSubfolder()
    {
        _dialogs.Choice = 0;
        var vm = CreateViewModel();

        await vm.OpenPathAsync(CreateDatasetFolder());

        Assert.Equal(["По подпапкам: подпапка — одна таблица", "Объединить всё в одну таблицу", "Каждый файл — отдельная таблица"],
            _dialogs.LastOptions);
        Assert.Equal(["price", "sellers", "sales_v2_parts"], vm.Model.Tables.Select(t => t.TableName));
        Assert.Equal(3, vm.Model.GetTable("sales_v2_parts")!.Rows.Count);
    }

    [Fact]
    public async Task FolderWithSubfolders_CombineAll_IncludesNestedFiles()
    {
        _dialogs.Choice = 1;
        var vm = CreateViewModel();

        await vm.OpenPathAsync(CreateDatasetFolder());

        var source = Assert.Single(vm.Model.Sources);
        Assert.True(source.IncludeSubfolders);
        var files = vm.Model.GetTable("dataset")!.Rows.Cast<System.Data.DataRow>().Select(r => (string)r[FolderImporter.FileColumn]).Distinct();
        Assert.Contains("sales/sales_v2_parts/sales_2024_02.csv", files);
        Assert.Equal(5, vm.Model.GetTable("dataset")!.Rows.Count);
    }

    /// <summary>Дерево как текст: «папка/» для папок, отступ — уровень вложенности.</summary>
    private static List<string> Tree(IEnumerable<TableTreeNode> nodes, string indent = "") =>
        nodes.SelectMany(n => new[] { indent + n.Name + (n.IsFolder ? "/" : "") }
            .Concat(n.IsFolder ? Tree(n.Children, indent + "  ") : [])).ToList();

    [Fact]
    public async Task TableTree_SeparateFiles_MirrorsFolderStructure()
    {
        _dialogs.Choice = 2;
        var vm = CreateViewModel();
        await vm.OpenPathAsync(_files.Write("standalone.csv", "a\n1\n"));

        await vm.OpenPathAsync(CreateDatasetFolder());

        Assert.Equal(
        [
            "dataset/",
            "  sales/",
            "    sales_v2_parts/",
            "      sales_2024_01",
            "      sales_2024_02",
            "  price",
            "  sellers",
            "standalone",
        ], Tree(vm.Data.TableTree));
        // Последняя загруженная таблица выделена в дереве.
        Assert.Equal("sellers", vm.Data.SelectedNode?.Name);
    }

    [Fact]
    public async Task TableTree_BySubfolders_PutsCombinedTableIntoParentFolder()
    {
        _dialogs.Choice = 0;
        var vm = CreateViewModel();

        await vm.OpenPathAsync(CreateDatasetFolder());

        Assert.Equal(["dataset/", "  sales/", "    sales_v2_parts", "  price", "  sellers"], Tree(vm.Data.TableTree));
        Assert.Equal("dataset/sales", vm.Model.Sources.Single(s => s.TableName == "sales_v2_parts").Group);
    }

    [Fact]
    public async Task TableTree_RemovingFolder_RemovesAllItsTables()
    {
        _dialogs.Choice = 2;
        var vm = CreateViewModel();
        await vm.OpenPathAsync(_files.Write("standalone.csv", "a\n1\n"));
        await vm.OpenPathAsync(CreateDatasetFolder());

        vm.Data.SelectedNode = vm.Data.TableTree[0];
        Assert.Equal("Удалить папку", vm.Data.RemoveLabel);
        await vm.Data.RemoveTableCommand.ExecuteAsync(null);

        Assert.Equal(["standalone"], vm.Model.Tables.Select(t => t.TableName));
        Assert.Equal(["standalone"], Tree(vm.Data.TableTree));
    }

    [Fact]
    public async Task TableTree_FoldersAreSavedInReport()
    {
        _dialogs.Choice = 2;
        var vm = CreateViewModel();
        await vm.OpenPathAsync(CreateDatasetFolder());
        var reportPath = _files.PathOf("report.pbm");
        ReportSerializer.Save(new Models.ReportDefinition { Sources = vm.Model.Sources }, reportPath);

        var reopened = CreateViewModel();
        await reopened.OpenReportFileAsync(reportPath);

        Assert.Equal(Tree(vm.Data.TableTree), Tree(reopened.Data.TableTree));
    }

    [Fact]
    public async Task OpeningReport_OverLoadedData_AsksBeforeReplacing()
    {
        var reportPath = _files.PathOf("other.pbm");
        ReportSerializer.Save(new Models.ReportDefinition(), reportPath);
        var vm = CreateViewModel();
        await vm.OpenPathAsync(_files.Write("current.csv", "a\n1\n"));

        _dialogs.ConfirmResult = false;
        await vm.OpenReportFileAsync(reportPath);
        Assert.Equal(["current"], vm.Model.Tables.Select(t => t.TableName));
        Assert.Null(vm.ReportPath);

        _dialogs.ConfirmResult = true;
        await vm.OpenReportFileAsync(reportPath);
        Assert.Empty(vm.Model.Tables);
        Assert.Equal(reportPath, vm.ReportPath);
        Assert.Equal(2, _dialogs.ConfirmCount);
    }

    [Fact]
    public async Task RemovingTable_AsksAndKeepsItWhenDeclined()
    {
        var vm = CreateViewModel();
        await vm.OpenPathAsync(_files.Write("sales.csv", "Регион,Сумма\nСевер,10\nЮг,20\n"));

        _dialogs.ConfirmResult = false;
        await vm.Data.RemoveTableCommand.ExecuteAsync(null);
        Assert.Equal(["sales"], vm.Model.Tables.Select(t => t.TableName));

        _dialogs.ConfirmResult = true;
        await vm.Data.RemoveTableCommand.ExecuteAsync(null);
        Assert.Empty(vm.Model.Tables);
        Assert.Equal(2, _dialogs.ConfirmCount);
    }

    [Fact]
    public async Task RemovingGroupByStep_AsksBecauseItDeletesTable_OtherStepsDoNot()
    {
        var vm = CreateViewModel();
        await vm.OpenPathAsync(_files.Write("sales.csv", "Регион,Сумма\nСевер,10\nЮг,20\n"));
        vm.Model.AddStep(new Models.RenameColumnStep { Table = "sales", Column = "Сумма", NewName = "Выручка" });
        vm.Model.AddStep(new Models.GroupByStep
        {
            Table = "sales", NewTable = "Итоги", GroupColumns = ["Регион"],
            Aggregations = [new Models.AggregationSpec { Column = "Выручка", Aggregation = Models.Aggregation.Sum }],
        });

        _dialogs.ConfirmResult = false;
        await vm.Transform.RemoveStepAsync(vm.Transform.Steps[1]);
        Assert.NotNull(vm.Model.GetTable("Итоги"));
        Assert.Equal(1, _dialogs.ConfirmCount);

        await vm.Transform.RemoveStepAsync(vm.Transform.Steps[0]);
        Assert.Equal(1, _dialogs.ConfirmCount); // обычный шаг удаляется без вопроса
        Assert.Single(vm.Model.Steps);

        _dialogs.ConfirmResult = true;
        await vm.Transform.RemoveStepAsync(vm.Transform.Steps[0]);
        Assert.Null(vm.Model.GetTable("Итоги"));
        Assert.Empty(vm.Model.Steps);
    }

    [Fact]
    public async Task OpeningReport_IntoEmptyWindow_DoesNotAsk()
    {
        var reportPath = _files.PathOf("first.pbm");
        ReportSerializer.Save(new Models.ReportDefinition(), reportPath);
        var vm = CreateViewModel();

        await vm.OpenReportFileAsync(reportPath);

        Assert.Equal(0, _dialogs.ConfirmCount);
        Assert.Equal(reportPath, vm.ReportPath);
    }

    [Fact]
    public async Task FolderWithOnlySubfolder_IsNotEmpty()
    {
        // Раньше папка «sales», где файлы лежат только в подпапке, считалась пустой.
        _dialogs.Choice = 0;
        var vm = CreateViewModel();

        await vm.OpenPathAsync(Path.Combine(CreateDatasetFolder(), "sales"));

        Assert.Equal(["sales_v2_parts"], vm.Model.Tables.Select(t => t.TableName));
    }

    [Fact]
    public async Task Folder_Separate_ImportsEachFile()
    {
        var folder = CreatePartsFolder();
        _dialogs.Choice = 1;
        var vm = CreateViewModel();

        await vm.OpenPathAsync(folder);

        Assert.Equal(["sales_2024_01", "sales_2024_02"], vm.Model.Tables.Select(t => t.TableName));
    }

    [Fact]
    public async Task Folder_Cancel_ImportsNothing()
    {
        var folder = CreatePartsFolder();
        _dialogs.Choice = -1;
        var vm = CreateViewModel();

        await vm.OpenPathAsync(folder);

        Assert.Empty(vm.Model.Sources);
    }

    [Fact]
    public async Task Folder_WithoutData_ShowsMessage()
    {
        var folder = Path.Combine(_files.Directory, "empty");
        Directory.CreateDirectory(folder);
        var vm = CreateViewModel();

        await vm.OpenPathAsync(folder);

        Assert.Empty(vm.Model.Sources);
        Assert.Equal("Нет данных", _dialogs.LastMessageTitle);
    }

    [Fact]
    public async Task ImportingSecondTable_WithOtherColumns_UpdatesProfile()
    {
        // Регрессия: профиль столбца считался по строкам предыдущей таблицы и падал с ArgumentException.
        var first = _files.Write("price.csv", "ID_PRODUCT,Price\n1,1.5\n2,2.5\n");
        var second = _files.Write("sellers.csv", "ID_OUTLET,Retail Chain\n1,DailyMart\n");
        var vm = CreateViewModel();
        await vm.OpenPathAsync(first);
        vm.Data.ProfileColumn = "Price";
        vm.Data.FilterColumn = "Price";
        vm.Data.FilterValue = "2";
        vm.Data.AddFilterCommand.Execute(null);

        await vm.OpenPathAsync(second);

        Assert.Equal("sellers", vm.Data.SelectedTable?.Name);
        Assert.Equal("ID_OUTLET", vm.Data.ProfileColumn);
        Assert.Contains(vm.Data.Profile, p => p.Label == "Строк" && p.Value == "1");
        Assert.Empty(vm.Data.Filters);

        vm.Data.SelectTable("price");
        Assert.Contains(vm.Data.Profile, p => p.Label == "Строк" && p.Value == "2");
    }

    [Fact]
    public void Theme_ToggleSwitchesAndPersists()
    {
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ToggleThemeCommand.Execute(null);

        Assert.Equal(AppTheme.Dark, _theme.Theme);
        Assert.True(vm.IsDarkThemeSelected);
        Assert.Contains(nameof(MainWindowViewModel.IsDarkTheme), changed);
        Assert.Equal(AppTheme.Dark, AppSettings.Load(_files.PathOf("settings.json")).Theme);

        vm.ToggleThemeCommand.Execute(null);
        vm.SetThemeCommand.Execute(AppTheme.System);
        Assert.True(vm.IsSystemTheme);
        Assert.Equal(AppTheme.System, AppSettings.Load(_files.PathOf("settings.json")).Theme);
    }

    private sealed class FakeTheme : IThemeService
    {
        public AppTheme Theme { get; set; } = AppTheme.System;
        public bool IsDark => Theme == AppTheme.Dark;
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public int Choice { get; set; }
        public IReadOnlyList<string>? LastOptions { get; private set; }
        public string? LastMessageTitle { get; private set; }

        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileTypeFilter> filters, bool allowMultiple) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> OpenFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedName, FileTypeFilter filter) => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>?> SelectItemsAsync(string title, string message, IReadOnlyList<string> items) =>
            Task.FromResult<IReadOnlyList<string>?>(items);

        public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> options)
        {
            LastOptions = options;
            return Task.FromResult(Choice);
        }

        public Task ShowMessageAsync(string title, string message)
        {
            LastMessageTitle = title;
            return Task.CompletedTask;
        }

        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCount { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmResult);
        }
    }
}
