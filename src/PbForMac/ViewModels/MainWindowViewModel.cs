using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PbForMac.Models;
using PbForMac.Services;
using PbForMac.Services.Importers;

namespace PbForMac.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private static readonly FileTypeFilter ReportFilter = new("Отчёт PbForMac", [ReportSerializer.Extension]);

    private readonly IDialogService _dialogs;

    public MainWindowViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        Report = new ReportViewModel(Model, ImportCommand, OpenSampleCommand);
        Data = new DataViewModel(Model, dialogs, ImportCommand);
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

    public bool IsReportPage => CurrentPage == Report;
    public bool IsDataPage => CurrentPage == Data;
    public bool IsModelPage => CurrentPage == Transform;

    public string Title => $"{(ReportPath is null ? "Новый отчёт" : Path.GetFileNameWithoutExtension(ReportPath))} — PbForMac";

    [RelayCommand]
    private void ShowReport() => CurrentPage = Report;

    [RelayCommand]
    private void ShowData() => CurrentPage = Data;

    [RelayCommand]
    private void ShowModel() => CurrentPage = Transform;

    [RelayCommand]
    private async Task ImportAsync()
    {
        var extensions = ImporterFactory.SupportedExtensions.ToList();
        var paths = await _dialogs.OpenFilesAsync("Получить данные",
        [
            new FileTypeFilter("Все поддерживаемые", extensions),
            new FileTypeFilter("CSV / TSV", [".csv", ".tsv", ".txt"]),
            new FileTypeFilter("Excel", [".xlsx", ".xlsm"]),
            new FileTypeFilter("JSON", [".json"]),
            new FileTypeFilter("XML", [".xml"]),
            new FileTypeFilter("SQLite", [".db", ".sqlite", ".sqlite3"]),
        ], allowMultiple: true);

        foreach (var path in paths)
            await ImportFileAsync(path);
    }

    /// <summary>Открывает отчёт .pbm или импортирует файл данных.</summary>
    public Task OpenPathAsync(string path) =>
        path.EndsWith(ReportSerializer.Extension, StringComparison.OrdinalIgnoreCase)
            ? OpenReportFileAsync(path)
            : ImportFileAsync(path);

    /// <summary>Импортирует файл (с выбором листов/таблиц) и возвращает имена новых таблиц.</summary>
    public async Task<IReadOnlyList<string>> ImportFileAsync(string path)
    {
        var names = new List<string>();
        try
        {
            IsBusy = true;
            Status = $"Загрузка {Path.GetFileName(path)}…";
            var kind = ImporterFactory.KindFor(path);
            var items = await Task.Run(() => ImporterFactory.Create(kind).ListItems(path));

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

            var fileName = Path.GetFileNameWithoutExtension(path);
            foreach (var item in selected)
            {
                var source = new DataSourceDefinition
                {
                    Kind = kind,
                    Path = Path.GetFullPath(path),
                    Item = item,
                    TableName = Model.UniqueTableName(items.Count > 1 ? item! : fileName),
                };
                var table = await Task.Run(() => ImporterFactory.Load(source));
                Model.AddSource(source, table);
                names.Add(source.TableName);
            }

            if (names.Count > 0)
            {
                Status = $"Загружено: {string.Join(", ", names)}";
                Data.SelectTable(names[^1]);
                if (Report.IsEmpty)
                    CurrentPage = Data;
            }
            else
            {
                Status = "Готово";
            }
        }
        catch (Exception e)
        {
            Status = "Ошибка загрузки";
            await _dialogs.ShowMessageAsync("Не удалось загрузить данные", $"{Path.GetFileName(path)}: {e.Message}");
        }
        finally
        {
            IsBusy = false;
        }
        return names;
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
        var paths = await _dialogs.OpenFilesAsync("Открыть отчёт", [ReportFilter], allowMultiple: false);
        if (paths.Count > 0)
            await OpenReportFileAsync(paths[0]);
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
