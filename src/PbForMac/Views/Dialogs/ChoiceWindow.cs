using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PbForMac.Views.Dialogs;

/// <summary>
/// Сообщение с несколькими вариантами действий (кнопки во всю ширину, первый — основной).
/// Результат — индекс варианта или null при отмене/закрытии окна.
/// </summary>
public sealed class ChoiceWindow : DialogWindow
{
    public ChoiceWindow() : this("", "", [])
    {
    }

    public ChoiceWindow(string title, string message, IReadOnlyList<string> options) : base(title)
    {
        Width = 460;
        var choices = new StackPanel { Spacing = 8, Margin = new Thickness(0, 16, 0, 0) };
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var button = CreateButton(options[i], i == 0, () => Close((int?)index));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(12, 8);
            choices.Children.Add(button);
        }

        var body = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                choices,
            },
        };
        Build(body, CreateButton("Отмена", false, () => Close(null)));
    }
}
