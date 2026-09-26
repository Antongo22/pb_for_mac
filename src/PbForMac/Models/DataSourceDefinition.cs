namespace PbForMac.Models;

public enum SourceKind
{
    Csv,
    Excel,
    Json,
    Xml,
    Sqlite,
    Folder,
}

/// <summary>Описание источника данных: откуда и что загружать в таблицу модели.</summary>
public sealed class DataSourceDefinition
{
    public SourceKind Kind { get; set; }

    /// <summary>Абсолютный путь к файлу или папке.</summary>
    public string Path { get; set; } = "";

    /// <summary>Путь относительно файла отчёта (заполняется при сохранении).</summary>
    public string? RelativePath { get; set; }

    /// <summary>Лист Excel или таблица SQLite; null, если в файле один набор данных.</summary>
    public string? Item { get; set; }

    /// <summary>Для папки: объединять также файлы из подпапок.</summary>
    public bool IncludeSubfolders { get; set; }

    /// <summary>Имя таблицы в модели.</summary>
    public string TableName { get; set; } = "";
}
