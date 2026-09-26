using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PbForMac.Views.Dialogs;

/// <summary>Выбор нескольких элементов (листов Excel, таблиц SQLite) флажками.</summary>
public sealed class SelectItemsWindow : DialogWindow
{
    public SelectItemsWindow() : this("", "", [])
    {
    }

    public SelectItemsWindow(string title, string message, IReadOnlyList<string> items) : base(title)
    {
        var boxes = items.Select((item, i) => new CheckBox { Content = item, IsChecked = i == 0 }).ToList();
        var list = new StackPanel { Spacing = 2 };
        list.Children.AddRange(boxes);

        var body = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new ScrollViewer { Content = list, MaxHeight = 320, Padding = new Thickness(0, 0, 12, 0) },
            },
        };

        Build(body,
            CreateButton("Выбрать все", false, () => boxes.ForEach(b => b.IsChecked = true)),
            CreateButton("Отмена", false, () => Close(null)),
            CreateButton("Загрузить", true, () =>
                Close(boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToList())));
    }
}
