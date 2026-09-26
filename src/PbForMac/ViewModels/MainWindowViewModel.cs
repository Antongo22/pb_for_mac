using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Models;
using PbForMac.Services;
using PbForMac.Services.Importers;
using PbForMac.ViewModels.Visuals;

namespace PbForMac.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private static readonly FileTypeFilter ReportFilter = new("Отчёт PbForMac", [ReportSerializer.Extension]);

    private static readonly IReadOnlyList<FileTypeFilter> DataFilters =
    [
        new("Все поддерживаемые данные", ImporterFactory.SupportedExtensions.ToList()),
        new("CSV / TSV", [".csv", ".tsv", ".txt"]),
        new("Excel", [".xlsx", ".xlsm"]),
        new("JSON", [".json"]),
        new("XML", [".xml"]),
        new("SQLite", [".db", ".sqlite", ".sqlite3"]),
    ];

    private readonly IDialogService _dialogs;
    private readonly IThemeService _theme;
    private readonly AppSettings _settings;

    public MainWindowViewModel(IDialogService dialogs, IThemeService theme, AppSettings settings)
    {
        _dialogs = dialogs;
        _theme = theme;
        _settings = settings;
        _theme.Changed += (_, _) => OnPropertyChanged(nameof(IsDarkTheme));
        Report = new ReportViewModel(Model, dialogs, ImportCommand, ImportFolderCommand, OpenSampleCommand);
        Data = new DataViewModel(Model, dialogs, ImportCommand, ImportFolderCommand);
        Transform = new TransformViewModel(Model, dialogs);
        _currentPage = Report;
        Model.Changed += (_, _) => RefreshModelSummary();
        Report.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ReportViewModel.SelectedVisual) or nameof(ReportViewModel.HasSelection))
                RefreshSelectionStatus();
        };
        RefreshModelSummary();
    }

    public DataModel Model { get; } = new();
    public ReportViewModel Report { get; }
    public DataViewModel Data { get; }
    public TransformViewModel Transform { get; }

    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "samples", "demo" + ReportSerializer.Extension);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReportPage), nameof(IsDataPage), nameof(IsModelPage))]
    private ViewModelBase _currentPage;

    [ObservableProperty]
    private string _status = "Готово";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _modelSummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string? _reportPath;

    public bool IsDarkTheme => _theme.IsDark;
    public bool IsSystemTheme => _theme.Theme == AppTheme.System;
    public bool IsLightTheme => _theme.Theme == AppTheme.Light;
    public bool IsDarkThemeSelected => _theme.Theme == AppTheme.Dark;

    public bool IsReportPage => CurrentPage == Report;
    public bool IsDataPage => CurrentPage == Data;
    public bool IsModelPage => CurrentPage == Transform;

    public string Title => $"{(ReportPath is null ? "Новый отчёт" : Path.GetFileNameWithoutExtension(ReportPath))} — PbForMac";

    /// <summary>Быстрое переключение между светлой и тёмной темой.</summary>
    [RelayCommand]
    private void ToggleTheme() => SetTheme(_theme.IsDark ? AppTheme.Light : AppTheme.Dark);

    [RelayCommand]
    private void SetTheme(AppTheme theme)
    {
        _theme.Theme = theme;
        _settings.Theme = theme;
        _settings.Save();
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkThemeSelected));
        Status = theme switch
        {
            AppTheme.Light => "Светлая тема",
            AppTheme.Dark => "Тёмная тема",
            _ => "Тема как в системе",
        };
    }

    [RelayCommand]
    private void ShowReport()
    {
        CurrentPage = Report;
        RefreshSelectionStatus();
    }

    [RelayCommand]
    private void ShowData()
    {
        CurrentPage = Data;
        Status = Data.SelectedTable is { } table
            ? $"Данные · {table.Name}"
            : "Данные";
    }

    [RelayCommand]
    private void ShowModel()
    {
        CurrentPage = Transform;
        Status = Transform.SelectedTable is { } table
            ? $"Модель · {table}"
            : "Модель";
    }

    private void RefreshModelSummary()
    {
        var tables = Model.Tables.Count;
        if (tables == 0)
        {
            ModelSummary = "";
            return;
        }

        var rows = Model.Tables.Sum(t => t.Rows.Count);
        var steps = Model.Steps.Count;
        var links = Model.Relationships.Count;
        var visuals = Report.Visuals.Count;
        var parts = new List<string> { $"Таблиц: {tables}", $"Строк: {rows:N0}" };
        if (steps > 0)
            parts.Add($"Шагов: {steps}");
        if (links > 0)
            parts.Add($"Связей: {links}");
        if (visuals > 0)
            parts.Add($"Визуалов: {visuals}");
        ModelSummary = string.Join("  ·  ", parts);
    }

    private void RefreshSelectionStatus()
    {
        if (CurrentPage != Report || IsBusy)
            return;
        if (Report.SelectedVisual is { } visual)
            Status = $"Выбран: {visual.KindLabel} — {visual.DisplayTitle}";
        else if (Model.Tables.Count > 0)
            Status = "Отчёт · выберите визуал или добавьте новый справа";
        else
            Status = "Готово";
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var paths = await _dialogs.OpenFilesAsync("Получить данные", DataFilters, allowMultiple: true);
        foreach (var path in paths)
            await ImportFileAsync(path);
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var folder = await _dialogs.OpenFolderAsync("Получить данные из папки");
        if (folder is not null)
            await ImportFolderPathAsync(folder);
    }

    /// <summary>Открывает отчёт .pbm, импортирует файл данных или папку.</summary>
    public Task OpenPathAsync(string path)
    {
        if (Directory.Exists(path))
            return ImportFolderPathAsync(path);
        return path.EndsWith(ReportSerializer.Extension, StringComparison.OrdinalIgnoreCase)
            ? OpenReportFileAsync(path)
            : ImportFileAsync(path);
    }

    /// <summary>
    /// Импортирует файл; если в нём несколько листов или таблиц, предлагает выбрать
    /// (они попадут в папку с именем файла). <paramref name="group"/> — папка в списке таблиц.
    /// </summary>
    public async Task ImportFileAsync(string path, string? group = null)
    {
        var sources = new List<DataSourceDefinition>();
        try
        {
            IsBusy = true;
            Status = $"Чтение {Path.GetFileName(path)}…";
            var kind = ImporterFactory.KindFor(path);
            var items = await Task.Run(() => ImporterFactory.Create(kind).ListItems(path));
            IsBusy = false;

            IReadOnlyList<string?> selected;
            if (items.Count <= 1)
            {
                selected = [items.FirstOrDefault()];
            }
            else
            {
                var chosen = await _dialogs.SelectItemsAsync("Выбор данных",
                    $"Выберите {(kind == SourceKind.Excel ? "листы" : "таблицы")} из «{Path.GetFileName(path)}»:", items);
                selected = chosen ?? [];
            }

            sources.AddRange(selected.Select(item => new DataSourceDefinition
            {
                Kind = kind,
                Path = Path.GetFullPath(path),
                Item = item,
                TableName = items.Count > 1 ? item! : Path.GetFileNameWithoutExtension(path),
                Group = items.Count > 1 ? JoinGroup(group, Path.GetFileName(path)) : group,
            }));
        }
        catch (Exception e)
        {
            IsBusy = false;
            Status = "Ошибка загрузки";
            await _dialogs.ShowMessageAsync("Не удалось загрузить данные", $"{Path.GetFileName(path)}: {e.Message}");
            return;
        }

        await AddSourcesAsync(Path.GetFileName(path), sources);
    }

    /// <summary>
    /// Импортирует папку вместе с подпапками. Пользователь выбирает способ:
    /// по подпапкам (файлы папки — отдельные таблицы, каждая подпапка — одна объединённая таблица),
    /// всё в одну таблицу (как источник «Папка» в Power BI) или каждый файл отдельной таблицей.
    /// </summary>
    public async Task ImportFolderPathAsync(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var folderName = Path.GetFileName(folder);
        var files = FolderImporter.FindFiles(folder, recursive: true);
        if (files.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Нет данных",
                $"В папке «{folderName}» и её подпапках нет файлов CSV, Excel, JSON, XML или SQLite.");
            return;
        }
        if (files.Count == 1)
        {
            await ImportFileAsync(files[0]);
            return;
        }

        // Файлы, сгруппированные по папкам, в которых они лежат.
        var groups = files
            .GroupBy(f => Path.GetDirectoryName(f)!)
            .Select(g => (Directory: g.Key, Files: g.ToList()))
            .ToList();
        var rootFiles = groups.FirstOrDefault(g => g.Directory == folder).Files ?? [];
        var subfolders = groups.Where(g => g.Directory != folder).ToList();
        var combinable = FolderImporter.FindFiles(folder, combinableOnly: true, recursive: true);
        // Таблицы раскладываются в списке по папкам так же, как файлы лежат на диске.
        string GroupFor(string directory) =>
            directory == folder ? folderName : JoinGroup(folderName, FolderImporter.RelativeName(folder, directory));

        var message = $"В папке «{folderName}» найдено файлов: {files.Count} ({Summary(files)}).";
        if (subfolders.Count > 0)
        {
            message += $"\nВ самой папке: {rootFiles.Count}, в подпапках: {files.Count - rootFiles.Count} — " +
                       string.Join(", ", subfolders.Select(g => $"{FolderImporter.RelativeName(folder, g.Directory)} ({g.Files.Count})")) + ".";
        }

        var options = new List<(string Label, Func<Task> Action)>();
        if (subfolders.Count > 0)
        {
            message += "\n\n• По подпапкам — файлы из самой папки станут отдельными таблицами, " +
                       "а файлы каждой подпапки объединятся в одну таблицу с её именем.";
            options.Add(("По подпапкам: подпапка — одна таблица", () => ImportBySubfoldersAsync(folder, groups, GroupFor)));
        }
        if (combinable.Count > 1)
        {
            message += "\n\n• Одна таблица — строки всех файлов" + (subfolders.Count > 0 ? ", включая подпапки," : "") +
                       " сложатся по совпадающим столбцам, столбец «Файл» покажет источник строки. " +
                       "Кнопка «Обновить» подхватит новые файлы, появившиеся в папке.";
            if (combinable.Count < files.Count)
                message += " Базы SQLite в объединение не входят.";
            options.Add(("Объединить всё в одну таблицу", () => AddSourcesAsync(folderName,
            [
                new DataSourceDefinition
                {
                    Kind = SourceKind.Folder,
                    Path = folder,
                    TableName = folderName,
                    IncludeSubfolders = subfolders.Count > 0,
                },
            ])));
        }
        options.Add(("Каждый файл — отдельная таблица", async () =>
        {
            foreach (var file in files)
                await ImportFileAsync(file, GroupFor(Path.GetDirectoryName(file)!));
        }));

        var choice = await _dialogs.ChooseAsync("Данные из папки", message, options.Select(o => o.Label).ToList());
        if (choice >= 0 && choice < options.Count)
            await options[choice].Action();
    }

    /// <summary>Файлы самой папки — отдельными таблицами, каждая подпапка — одной объединённой таблицей.</summary>
    private async Task ImportBySubfoldersAsync(string folder, IReadOnlyList<(string Directory, List<string> Files)> groups,
        Func<string, string> groupFor)
    {
        foreach (var (directory, files) in groups)
        {
            var combinable = files.Where(f => ImporterFactory.KindFor(f) != SourceKind.Sqlite).ToList();
            if (directory == folder || combinable.Count < 2)
            {
                foreach (var file in files)
                    await ImportFileAsync(file, groupFor(directory));
                continue;
            }

            // Объединённая подпапка становится таблицей внутри родительской папки.
            await AddSourcesAsync(Path.GetFileName(directory),
            [
                new DataSourceDefinition
                {
                    Kind = SourceKind.Folder,
                    Path = directory,
                    TableName = Path.GetFileName(directory),
                    Group = groupFor(Path.GetDirectoryName(directory)!),
                },
            ]);
            // Базы SQLite из подпапки загружаются отдельно: их таблицы не объединяются построчно.
            foreach (var file in files.Except(combinable))
                await ImportFileAsync(file, groupFor(directory));
        }
    }

    private static string JoinGroup(string? parent, string name) =>
        string.IsNullOrEmpty(parent) ? name : $"{parent}/{name}";

    private static string Summary(IEnumerable<string> files) => string.Join(", ", files
        .GroupBy(f => Path.GetExtension(f).TrimStart('.').ToUpperInvariant())
        .Select(g => $"{g.Key}: {g.Count()}"));

    /// <summary>Загружает источники в модель и показывает результат на странице «Данные».</summary>
    private async Task AddSourcesAsync(string displayName, IReadOnlyList<DataSourceDefinition> sources)
    {
        if (sources.Count == 0)
            return;
        var names = new List<string>();
        try
        {
            IsBusy = true;
            Status = $"Загрузка {displayName}…";
            foreach (var source in sources)
            {
                source.TableName = Model.UniqueTableName(source.TableName);
                var table = await Task.Run(() => ImporterFactory.Load(source));
                Model.AddSource(source, table);
                names.Add(source.TableName);
            }
        }
        catch (Exception e)
        {
            Status = "Ошибка загрузки";
            await _dialogs.ShowMessageAsync("Не удалось загрузить данные", $"{displayName}: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }

        if (names.Count > 0)
        {
            // Как Power BI: связи с новыми таблицами ищутся сразу после загрузки.
            var relationships = Model.DetectRelationships(names);
            Status = $"Загружено: {string.Join(", ", names)}" +
                     (relationships.Count > 0 ? $" · найдено связей: {relationships.Count} (страница «Модель» → «Связи»)" : "");
            Data.SelectTable(names[^1]);
            if (Report.IsEmpty)
                CurrentPage = Data;
        }
    }

    /// <summary>Отчёт с данными или визуалами можно закрыть только после подтверждения.</summary>
    private async Task<bool> ConfirmCloseCurrentAsync(string title, string action)
    {
        if (Model.Sources.Count == 0 && Report.IsEmpty)
            return true;
        return await _dialogs.ConfirmAsync(title,
            $"{action} Текущий отчёт будет закрыт, несохранённые изменения будут потеряны. Продолжить?");
    }

    [RelayCommand]
    private async Task NewReportAsync()
    {
        if (!await ConfirmCloseCurrentAsync("Новый отчёт", "Создать новый отчёт?"))
            return;
        Report.Clear();
        Model.Clear();
        ReportPath = null;
        CurrentPage = Report;
        Status = "Создан новый отчёт";
    }

    [RelayCommand]
    private async Task OpenReportAsync()
    {
        // Открыть можно и отчёт, и файлы данных; папки — через «Получить данные → Папка…».
        var paths = await _dialogs.OpenFilesAsync("Открыть отчёт или данные",
            [new FileTypeFilter("Отчёты и данные", [ReportSerializer.Extension, .. ImporterFactory.SupportedExtensions]), ReportFilter, .. DataFilters],
            allowMultiple: true);
        foreach (var path in paths)
            await OpenPathAsync(path);
    }

    [RelayCommand]
    private async Task OpenSampleAsync()
    {
        if (File.Exists(SamplePath))
            await OpenReportFileAsync(SamplePath);
        else
            await _dialogs.ShowMessageAsync("Пример не найден", $"Файл примера отсутствует: {SamplePath}");
    }

    public async Task OpenReportFileAsync(string path)
    {
        if (!await ConfirmCloseCurrentAsync("Открыть отчёт", $"Открыть «{Path.GetFileNameWithoutExtension(path)}»?"))
            return;
        try
        {
            IsBusy = true;
            Status = $"Открытие {Path.GetFileName(path)}…";
            var report = ReportSerializer.Load(path);
            Report.Clear();
            var errors = Model.Load(report.Sources, report.Steps, report.Relationships, report.TableLayouts);
            Report.LoadPages(report.Pages);
            Report.LoadBookmarks(report.Bookmarks);
            ReportPath = path;
            CurrentPage = Report;
            Status = $"Открыт отчёт: {Path.GetFileName(path)}";
            if (errors.Count > 0)
                await _dialogs.ShowMessageAsync("Не все источники загружены", string.Join("\n", errors));
        }
        catch (Exception e)
        {
            Status = "Ошибка открытия отчёта";
            await _dialogs.ShowMessageAsync("Не удалось открыть отчёт", e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveReportAsync()
    {
        if (ReportPath is null)
            await SaveReportAsAsync();
        else
            await SaveToAsync(ReportPath);
    }

    [RelayCommand]
    private async Task SaveReportAsAsync()
    {
        var path = await _dialogs.SaveFileAsync("Сохранить отчёт", "Отчёт" + ReportSerializer.Extension, ReportFilter);
        if (path is not null)
            await SaveToAsync(path);
    }

    private async Task SaveToAsync(string path)
    {
        try
        {
            if (!path.EndsWith(ReportSerializer.Extension, StringComparison.OrdinalIgnoreCase))
                path += ReportSerializer.Extension;
            ReportSerializer.Save(new ReportDefinition
            {
                Sources = Model.Sources,
                Steps = Model.Steps,
                Relationships = Model.Relationships,
                TableLayouts = Model.TableLayouts,
                Pages = Report.GetPages(),
                Bookmarks = Report.GetBookmarks(),
            }, path);
            ReportPath = path;
            Status = $"Сохранено: {path}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync("Не удалось сохранить отчёт", e.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Model.Sources.Count == 0)
            return;
        IsBusy = true;
        Status = "Обновление данных…";
        var errors = Model.Reload();
        IsBusy = false;
        Status = errors.Count == 0 ? $"Данные обновлены ({DateTime.Now:HH:mm:ss})" : "Обновлено с ошибками";
        if (errors.Count > 0)
            await _dialogs.ShowMessageAsync("Ошибки обновления", string.Join("\n", errors));
    }

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        CurrentPage = Report;
        var name = ReportPath is null ? "Отчёт" : Path.GetFileNameWithoutExtension(ReportPath);
        var path = await _dialogs.SaveFileAsync("Экспорт дашборда в PNG", name + ".png", new FileTypeFilter("PNG", [".png"]));
        if (path is null)
            return;
        try
        {
            await Report.ExportPngAsync(path);
            Status = $"Экспортировано: {path}";
        }
        catch (Exception e)
        {
            await _dialogs.ShowMessageAsync("Не удалось экспортировать", e.Message);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (!TryGetExcelExportSource(out var table, out var rows, out var suggestedName))
        {
            await _dialogs.ShowMessageAsync("Экспорт в Excel", "Нет таблицы или визуала для экспорта.");
            return;
        }

        var path = await _dialogs.SaveFileAsync("Экспорт в Excel", suggestedName + ".xlsx",
            new FileTypeFilter("Excel", [".xlsx"]));
        if (path is null)
            return;
        try
        {
            if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                path += ".xlsx";
            ExcelExporter.Export(table, rows, path);
            Status = $"Экспортировано: {path}";
        }
        catch (Exception e)
        {
            await _dialogs.ShowMessageAsync("Не удалось экспортировать", e.Message);
        }
    }

    private bool TryGetExcelExportSource(out DataTable table, out IEnumerable<System.Data.DataRow> rows, out string suggestedName)
    {
        table = null!;
        rows = [];
        suggestedName = "Данные";

        if (CurrentPage == Report && Report.SelectedVisual is TableVisualViewModel { Slice: { } tableSlice })
        {
            table = tableSlice.Table;
            rows = tableSlice.Rows;
            suggestedName = string.IsNullOrWhiteSpace(Report.SelectedVisual.DisplayTitle)
                ? table.TableName
                : Report.SelectedVisual.DisplayTitle;
            return true;
        }
        if (CurrentPage == Report && Report.SelectedVisual is MatrixVisualViewModel { Slice: { } matrixSlice })
        {
            table = matrixSlice.Table;
            rows = matrixSlice.Rows;
            suggestedName = string.IsNullOrWhiteSpace(Report.SelectedVisual.DisplayTitle)
                ? table.TableName
                : Report.SelectedVisual.DisplayTitle;
            return true;
        }

        var modelTable = Data.SelectedTable is { } item
            ? Model.GetTable(item.Name)
            : Model.Tables.FirstOrDefault();
        if (modelTable is null)
            return false;

        table = modelTable;
        rows = modelTable.Rows.Cast<System.Data.DataRow>();
        suggestedName = modelTable.TableName;
        return true;
    }
}
