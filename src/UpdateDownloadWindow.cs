using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace AsterVPN;

internal sealed class UpdateDownloadWindow : Window, IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 12, Margin = new Thickness(0, 16, 0, 12) };
    private readonly TextBlock status = new() { Text = "Подключение к серверу…", TextWrapping = TextWrapping.Wrap };
    private bool finished;
    internal CancellationToken Token => cancellation.Token;
    internal UpdateDownloadWindow(Window owner, string version)
    {
        Owner = owner; Title = "Загрузка обновления"; Width = 460;
        SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = owner.Background; Foreground = owner.Foreground;
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = $"Скачивание Aster VPN {version}", FontSize = 20 });
        content.Children.Add(bar); content.Children.Add(status);
        var cancel = new Button { Content = "Отмена", HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        cancel.Click += (_, _) => Close(); content.Children.Add(cancel); Content = content;
        Closing += (_, _) => { if (!finished) cancellation.Cancel(); };
    }
    internal void Report(AppUpdater.DownloadProgress progress)
    {
        if (finished || cancellation.IsCancellationRequested) return;
        var percent = progress.Total > 0 ? Math.Clamp(100.0 * progress.Received / progress.Total, 0, 100) : 0;
        bar.Value = percent;
        status.Text = progress.Verifying ? "Загрузка завершена. Проверка SHA-256…" : $"{percent:F0}% · {progress.Received / 1048576.0:F1} / {progress.Total / 1048576.0:F1} МиБ";
    }
    public void Dispose() { finished = true; Close(); cancellation.Dispose(); }
}
