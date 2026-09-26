namespace PbForMac.Services;

public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Extensions);

/// <summary>Диалоги, которые view-модели запрашивают у окна.</summary>
public interface IDialogService
{
    Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileTypeFilter> filters, bool allowMultiple);

    /// <summary>Выбор папки; null — пользователь отменил.</summary>
    Task<string?> OpenFolderAsync(string title);

    Task<string?> SaveFileAsync(string title, string suggestedName, FileTypeFilter filter);

    /// <summary>Выбор нескольких элементов из списка; null — пользователь отменил.</summary>
    Task<IReadOnlyList<string>?> SelectItemsAsync(string title, string message, IReadOnlyList<string> items);

    /// <summary>Выбор одного из вариантов кнопками; -1 — отмена.</summary>
    Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> options);

    Task ShowMessageAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message);
}
