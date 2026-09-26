using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PbForMac.Services;
using PbForMac.Views.Dialogs;

namespace PbForMac.Views;

/// <summary>Реализация диалогов через StorageProvider и модальные окна Avalonia.</summary>
public sealed class DialogService(Window owner) : IDialogService
{
    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<FileTypeFilter> filters, bool allowMultiple)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple,
            FileTypeFilter = filters.Select(ToPickerType).ToList(),
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> OpenFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Select(f => f.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName, FileTypeFilter filter)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = filter.Extensions.FirstOrDefault()?.TrimStart('.'),
            FileTypeChoices = [ToPickerType(filter)],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public Task<IReadOnlyList<string>?> SelectItemsAsync(string title, string message, IReadOnlyList<string> items) =>
        new SelectItemsWindow(title, message, items).ShowDialog<IReadOnlyList<string>?>(owner);

    // Закрытие окна крестиком или Esc даёт null — это отмена.
    public async Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> options) =>
        await new ChoiceWindow(title, message, options).ShowDialog<int?>(owner) ?? -1;

    public Task<string?> PromptAsync(string title, string message, string initial = "") =>
        new PromptWindow(title, message, initial).ShowDialog<string?>(owner);

    public Task ShowMessageAsync(string title, string message) =>
        new MessageWindow(title, message, confirm: false).ShowDialog<bool>(owner);

    public Task<bool> ConfirmAsync(string title, string message) =>
        new MessageWindow(title, message, confirm: true).ShowDialog<bool>(owner);

    private static FilePickerFileType ToPickerType(FileTypeFilter filter) => new(filter.Name)
    {
        Patterns = filter.Extensions.Select(e => "*" + e).ToList(),
    };
}
