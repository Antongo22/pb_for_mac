using Avalonia.Controls;
using Avalonia.Media;

namespace PbForMac.Views.Dialogs;

/// <summary>Сообщение или вопрос «Да/Нет». Результат диалога — true при подтверждении.</summary>
public sealed class MessageWindow : DialogWindow
{
    public MessageWindow() : this("", "", false)
    {
    }

    public MessageWindow(string title, string message, bool confirm) : base(title)
    {
        var text = new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxHeight = 400 };
        if (confirm)
            Build(text, CreateButton("Отмена", false, () => Close(false)), CreateButton("Да", true, () => Close(true)));
        else
            Build(text, CreateButton("OK", true, () => Close(true)));
    }
}
