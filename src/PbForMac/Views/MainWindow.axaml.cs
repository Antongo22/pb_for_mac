using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using PbForMac.ViewModels;

namespace PbForMac.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // На macOS горячие клавиши обрабатывает системное меню (NativeMenu), на остальных ОС — окно.
        if (OperatingSystem.IsMacOS() || DataContext is not MainWindowViewModel vm)
            return;
        KeyBindings.Clear();
        Add(Key.N, vm.NewReportCommand);
        Add(Key.O, vm.OpenReportCommand);
        Add(Key.S, vm.SaveReportCommand);
        Add(Key.S, vm.SaveReportAsCommand, KeyModifiers.Shift);
        Add(Key.I, vm.ImportCommand);
        Add(Key.R, vm.RefreshCommand);
        Add(Key.E, vm.ExportPngCommand);
        Add(Key.D1, vm.ShowReportCommand);
        Add(Key.D2, vm.ShowDataCommand);
        Add(Key.D3, vm.ShowModelCommand);

        void Add(Key key, System.Windows.Input.ICommand command, KeyModifiers extra = KeyModifiers.None) =>
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, KeyModifiers.Control | extra), Command = command });
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>Перетаскивание файлов в окно: .pbm открывается как отчёт, остальное импортируется.</summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        foreach (var path in paths)
            await vm.OpenPathAsync(path);
    }
}
