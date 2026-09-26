using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Models;
using PbForMac.Services;
using PbForMac.Services.Importers;

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
        Report = new ReportViewModel(Model, ImportCommand, ImportFolderCommand, OpenSampleCommand);
        Data = new DataViewModel(Model, dialogs, ImportCommand, ImportFolderCommand);
        Transform = new TransformViewModel(Model);
        _currentPage = Report;
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
    private void ShowReport() => CurrentPage = Report;

    [RelayCommand]
    private void ShowData() => CurrentPage = Data;

    [RelayCommand]
    private void ShowModel() => CurrentPage = Transform;

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

    /// <summary>Импортирует файл; если в нём несколько листов или таблиц, предлагает выбрать.</summary>
    public async Task ImportFileAsync(string path)
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
    /// Импортирует папку: объединяет файлы в одну таблицу (как источник «Папка» в Power BI)
    /// или загружает каждый файл отдельной таблицей — на выбор пользователя.
    /// </summary>
    public async Task ImportFolderPathAsync(string folder)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var folderName = Path.GetFileName(folder);
        var files = FolderImporter.FindFiles(folder);
        if (files.Count == 0)
        {
            await _dialogs.ShowMessageAsync("Нет данных",
                $"В папке «{folderName}» нет файлов CSV, Excel, JSON, XML или SQLite.");
            return;
        }
        if (files.Count == 1)
        {
            await ImportFileAsync(files[0]);
            return;
        }

        var combinable = FolderImporter.FindFiles(folder, combinableOnly: true);
        var summary = string.Join(", ", files
            .GroupBy(f => Path.GetExtension(f).TrimStart('.').ToUpperInvariant())
            .Select(g => $"{g.Key}: {g.Count()}"));
        var message = $"В папке «{folderName}» найдено файлов: {files.Count} ({summary}).";
        IReadOnlyList<string> options;
        if (combinable.Count > 1)
        {
            message += "\n\nОбъединение сложит строки всех файлов в одну таблицу по совпадающим столбцам " +
                       "и добавит столбец «Файл» с именем исходного файла. Кнопка «Обновить» подхватит новые файлы, " +
                       "появившиеся в папке.";
            if (combinable.Count < files.Count)
                message += " Базы SQLite в объединение не входят.";
            options = ["Объединить в одну таблицу", "Каждый файл — отдельная таблица"];
        }
        else
        {
            options = ["Каждый файл — отдельная таблица"];
        }

        var choice = await _dialogs.ChooseAsync("Данные из папки", message, options);
        if (choice < 0)
            return;
        if (combinable.Count > 1 && choice == 0)
        {
            await AddSourcesAsync(folderName,
                [new DataSourceDefinition { Kind = SourceKind.Folder, Path = folder, TableName = folderName }]);
            return;
        }
        foreach (var file in files)
            await ImportFileAsync(file);
    }

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
            Status = $"Загружено: {string.Join(", ", names)}";
            Data.SelectTable(names[^1]);
            if (Report.IsEmpty)
                CurrentPage = Data;
        }
    }

    [RelayCommand]
    private async Task NewReportAsync()
    {
        if (Model.Sources.Count > 0 || !Report.IsEmpty)
        {
            if (!await _dialogs.ConfirmAsync("Новый отчёт", "Закрыть текущий отчёт? Несохранённые изменения будут потеряны."))
                return;
        }
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
        try
        {
            IsBusy = true;
            Status = $"Открытие {Path.GetFileName(path)}…";
            var report = ReportSerializer.Load(path);
            Report.Clear();
            var errors = Model.Load(report.Sources, report.Steps);
            Report.Load(report.Visuals, report.Filters);
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
                Visuals = Report.GetVisualDefinitions(),
                Filters = Report.GetFilterDefinitions(),
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
}
