using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AsterVPN;

internal static class UpdatePrompt
{
    internal static bool Show(Window owner, string version)
    {
        var dialog = new Window
        {
            Title = "Доступно обновление Aster VPN", Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height, Width = 460,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Background = owner.Background, Foreground = owner.Foreground
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Доступна версия {version}", FontSize = 22, Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(new TextBlock { Text = $"Установлена версия {ApplicationVersion.Current}. Обновить сейчас?\nПеред установкой VPN будет отключён.", TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var later = new Button { Content = "Позже", IsCancel = true };
        var install = new Button { Content = "Обновить", IsDefault = true };
        later.Click += (_, _) => dialog.DialogResult = false;
        install.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(later); buttons.Children.Add(install);
        panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true;
    }
}
