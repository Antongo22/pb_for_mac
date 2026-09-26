using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace PbForMac.Views.Dialogs;

/// <summary>Общая основа простых модальных окон: заголовок, содержимое и кнопки.</summary>
public abstract class DialogWindow : Window
{
    protected DialogWindow(string title)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    protected void Build(Control body, params Button[] buttons)
    {
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttonRow.Children.AddRange(buttons);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { body, buttonRow },
        };
    }

    protected static Button CreateButton(string text, bool isDefault, Action onClick)
    {
        var button = new Button { Content = text, IsDefault = isDefault, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (isDefault)
            button.Classes.Add("accent");
        button.Click += (_, _) => onClick();
        return button;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Close();
    }
}
