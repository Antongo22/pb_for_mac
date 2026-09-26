using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PbForMac.Views.Dialogs;

/// <summary>Диалог ввода строки. Результат — текст или null при отмене.</summary>
public sealed class PromptWindow : DialogWindow
{
    private readonly TextBox _box;

    public PromptWindow() : this("", "", "")
    {
    }

    public PromptWindow(string title, string message, string initial) : base(title)
    {
        _box = new TextBox { Text = initial, Watermark = message };
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                _box,
            },
        };
        Build(body, CreateButton("Отмена", false, () => Close(null)), CreateButton("OK", true, () => Close(_box.Text?.Trim())));
        Opened += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }
}
