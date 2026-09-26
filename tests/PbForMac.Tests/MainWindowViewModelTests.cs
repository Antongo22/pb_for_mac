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

        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
    }
}
