using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;
namespace AsterVPN;
public partial class MainWindow : Window
{
    private void ServerListMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return;
        var distance = lines < 0 ? SubscriptionsScroll.ViewportHeight : lines * 16;
        SubscriptionsScroll.ScrollToVerticalOffset(SubscriptionsScroll.VerticalOffset - e.Delta / 120.0 * distance);
        e.Handled = true;
    }
    private Settings settings = new();
    private readonly ObservableCollection<string> apps = [];
    private readonly VpnEngine engine = new();
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly DispatcherTimer updateTimer;
    private readonly DispatcherTimer logTimer;
    private readonly Queue<string> pendingLogs = new();
    private DateTimeOffset nextUpdateAttempt = DateTimeOffset.MinValue;
    private bool busy, closing, closed, exitRequested;
    private readonly System.Drawing.Icon applicationIcon;
    private bool restoringSelection;
    private string? connectedSignature;
    private string? connectedName;
    private string? connectedSubscriptionId;
    private bool connecting;
    private ServerProfile? selectedServer;
    private readonly List<System.Windows.Controls.ListBox> serverLists = [];
    private static string Signature(ServerProfile server) => server.Name + server.Outbound.ToJsonString();
    public MainWindow(bool startup = false)
    {
        InitializeComponent();
        HeaderVersion.Text = $"   VPN  /  {ApplicationVersion.Current}";
        ProductVersionText.Text = $"Aster VPN {ApplicationVersion.Current} · sing-box 1.14.3 · Windows x64";
        try { HwidBox.Text = DeviceIdentity.Hwid; }
        catch { HwidBox.Text = "HWID недоступен: не удалось прочитать идентификатор Windows"; }
        engine.Status += status => Dispatcher.BeginInvoke(() =>
        {
            StatusText.Text = status;
            ConnectButton.ToolTip = connecting ? "Отменить подключение" : engine.IsWanted ? "Отключить или переключить на выбранный сервер" : "Подключить выбранный сервер";
            ActiveText.Text = connecting ? "Подключение: " + connectedName + "\nНажмите кнопку для отмены" : engine.IsWanted ? "Активный: " + connectedName : "Нажмите кнопку для подключения";
            PowerGlyph.Opacity = engine.IsWanted ? 1 : 0.65;
            PowerGlyph.Text = engine.State == ConnectionState.Connected ? "✓" : connecting ? "×" : "⏻";
            StatusText.Foreground = new SolidColorBrush(engine.State is ConnectionState.Error or ConnectionState.Degraded ? Color.FromRgb(230, 178, 92) : Color.FromRgb(233, 235, 227));
            SetConnectionAnimation(engine.IsTransitioning);
        });
        engine.Log += Log;
        logTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        logTimer.Tick += (_, _) => FlushLogs();
        logTimer.Start();
        using (var iconStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/aster.ico")).Stream)
        {
            using var loadedIcon = new System.Drawing.Icon(iconStream);
            applicationIcon = (System.Drawing.Icon)loadedIcon.Clone();
        }
        tray = new System.Windows.Forms.NotifyIcon { Icon = applicationIcon, Text = "Aster VPN", Visible = true };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Открыть Aster VPN", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Проверить обновления", null, async (_, _) => await CheckAppUpdateAsync(true));
        menu.Items.Add("Отключить", null, async (_, _) => await Run(engine.DisconnectAsync));
        menu.Items.Add("Выход", null, (_, _) => Dispatcher.Invoke(RequestExit));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
        AppList.ItemsSource = apps;
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        updateTimer.Tick += async (_, _) =>
        {
            if (!closing && DateTimeOffset.UtcNow >= nextAppUpdate) { nextAppUpdate = DateTimeOffset.UtcNow.AddHours(6); _ = CheckAppUpdateAsync(false); }
            if (busy || closing || !settings.AutoUpdate || DateTimeOffset.UtcNow < nextUpdateAttempt) return;
            var due = settings.Subscriptions.Where(sub => sub.Url.Length > 0 && (sub.LastUpdated == null || DateTimeOffset.UtcNow - sub.LastUpdated >= TimeSpan.FromHours(sub.UpdateIntervalHours))).ToArray();
            if (due.Length == 0) return;
            nextUpdateAttempt = DateTimeOffset.UtcNow.AddMinutes(15);
            foreach (var sub in due) await Run(() => UpdateSubscription(sub.Url, sub), false);
        };
        try { settings = SecureStore.Load(); settings.MigrateSubscriptions(); LoadSettings(); HomeNav.IsChecked = true; updateTimer.Start(); }
        catch (Exception ex) { Loaded += (_, _) => { MessageBox.Show("Не удалось прочитать защищённые настройки. Файл не перезаписан.\n" + ex.Message, "Aster VPN"); RequestExit(); }; }
        if (startup) { WindowState = WindowState.Minimized; ShowInTaskbar = false; }
        Loaded += async (_, _) =>
        {
            if (startup) Hide();
            nextAppUpdate = DateTimeOffset.UtcNow.AddHours(6);
            _ = CheckAppUpdateAsync(false);
            if (exitRequested) return;
            await Run(() => { engine.RestoreProtection(settings); return Task.CompletedTask; });
            if (settings.AutoConnect && selectedServer != null && !closing)
                await Run(ConnectSelectedAsync, notify: !startup);
        };
    }
    private DateTimeOffset nextAppUpdate = DateTimeOffset.MaxValue;
    private bool checkingAppUpdate;
    private async void CheckAppUpdateClick(object sender, RoutedEventArgs e) => await CheckAppUpdateAsync(true);
    private async Task CheckAppUpdateAsync(bool manual)
    {
        if (checkingAppUpdate || closing) return;
        checkingAppUpdate = true;
        CheckAppUpdateButton.IsEnabled = false;
        CheckAppUpdateButton.Content = "Проверка обновлений…";
        try
        {
            var update = await AppUpdater.CheckAsync();
            if (closing) return;
            if (update == null) { if (manual) MessageBox.Show("Новых обновлений нет.", "Aster VPN"); return; }
            ShowWindow();
            if (!UpdatePrompt.Show(this, update.Version)) return;
            var path = await AppUpdater.DownloadAsync(update);
            if (closing) return;
            // Wait for normal shutdown and tunnel cleanup before Inno checks AppMutex.
            var script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Process -FilePath '{path.Replace("'", "''")}'";
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded) { UseShellExecute = false, CreateNoWindow = true });
            RequestExit();
        }
        catch (Exception ex) { if (manual) MessageBox.Show("Не удалось обновить клиент: " + ex.Message, "Aster VPN"); else Log("Проверка обновления клиента не удалась: " + ex.Message); }
        finally { checkingAppUpdate = false; CheckAppUpdateButton.IsEnabled = true; CheckAppUpdateButton.Content = "Проверить обновления"; }
    }
    private void LoadSettings()
    {
        SubscriptionBox.Text = ""; ModeBox.SelectedIndex = settings.AppMode;
        CountriesBox.Text = settings.DirectCountries; LanBox.IsChecked = settings.BypassLan;
        DirectSitesBox.Text = settings.DirectSites; VpnSitesBox.Text = settings.VpnSites;
        SiteModeBox.SelectedIndex = settings.EffectiveSiteMode;
        GeoModeBox.SelectedIndex = settings.EffectiveGeoMode;
        UpdateRoutingModeControls();
        ReconnectBox.IsChecked = settings.AutoReconnect; AutoUpdateBox.IsChecked = settings.AutoUpdate; ProbeBox.Text = settings.ProbeUrl;
        KillSwitchBox.IsChecked = settings.KillSwitch; StartupBox.IsChecked = settings.StartWithWindows; AutoConnectBox.IsChecked = settings.AutoConnect;
        apps.Clear(); foreach (var app in settings.Apps) apps.Add(app); RefreshServers();
    }
    private void RefreshServers()
    {
        restoringSelection = true;
        selectedServer = settings.AllServers.FirstOrDefault(p => p.Id == settings.SelectedServerId) ?? settings.AllServers.FirstOrDefault();
        settings.SelectedServerId = selectedServer?.Id;
        serverLists.Clear(); GroupsList.ItemsSource = null; GroupsList.ItemsSource = settings.Subscriptions;
        restoringSelection = false;
        ApplySearch(); UpdateSelectionDisplay();
    }
    private void GroupListLoaded(object sender, RoutedEventArgs e)
    {
        var list = (System.Windows.Controls.ListBox)sender;
        if (!serverLists.Contains(list)) serverLists.Add(list);
        restoringSelection = true; list.SelectedItem = list.Items.Contains(selectedServer) ? selectedServer : null; restoringSelection = false;
        ApplySearch();
    }
    private void GroupExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (restoringSelection || !IsLoaded || closing) return;
        try { SecureStore.Save(settings); } catch { Log("Не удалось сохранить сворачивание подписки."); }
    }
    private void Capture()
    {
        ConfigBuilder.Countries(CountriesBox.Text);
        ConfigBuilder.Sites(DirectSitesBox.Text); ConfigBuilder.Sites(VpnSitesBox.Text);
        if (!Uri.TryCreate(ProbeBox.Text.Trim(), UriKind.Absolute, out var probe) || probe.Scheme != "https") throw new FormatException("Адрес проверки должен быть HTTPS.");
        settings.SelectedServerId = selectedServer?.Id;
        settings.AppMode = ModeBox.SelectedIndex; settings.Apps = apps.ToList(); settings.DirectCountries = CountriesBox.Text.Trim();
        settings.DirectSites = string.Join(Environment.NewLine, ConfigBuilder.Sites(DirectSitesBox.Text));
        settings.VpnSites = string.Join(Environment.NewLine, ConfigBuilder.Sites(VpnSitesBox.Text));
        settings.SiteMode = SiteModeBox.SelectedIndex; settings.GeoMode = GeoModeBox.SelectedIndex;
        settings.BypassLan = LanBox.IsChecked == true; settings.AutoReconnect = ReconnectBox.IsChecked == true;
        settings.AutoUpdate = AutoUpdateBox.IsChecked == true; settings.ProbeUrl = ProbeBox.Text.Trim();
        var startup = StartupBox.IsChecked == true;
        if (startup != settings.StartWithWindows) WindowsStartup.SetEnabled(startup);
        settings.StartWithWindows = startup;
        settings.KillSwitch = KillSwitchBox.IsChecked == true; settings.AutoConnect = AutoConnectBox.IsChecked == true;
        SecureStore.Save(settings);
    }
    private void RoutingModeChanged(object sender, SelectionChangedEventArgs e) => UpdateRoutingModeControls();
    private void UpdateRoutingModeControls()
    {
        // SelectionChanged also fires during InitializeComponent.
        if (SiteModeBox == null || VpnSitesBox == null || DirectSitesBox == null || CountriesBox == null || GeoModeBox == null) return;
        VpnSitesBox.Visibility = SiteModeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        DirectSitesBox.Visibility = SiteModeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        CountriesBox.IsEnabled = GeoModeBox.SelectedIndex != 0;
    }
    private async Task Run(Func<Task> action, bool notify = true)
    {
        if (busy || closing) return;
        busy = true;
        OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.IsIndeterminate = true;
        try { await action(); }
        catch (Exception ex)
        {
            // HTTP exceptions can contain the private subscription URL; never show them verbatim.
            var message = ex is System.Net.Http.HttpRequestException or TaskCanceledException ? "Сетевая операция не завершилась. Проверьте интернет, адрес и доступность сервера." : ex.Message;
            PingText.Text = message;
            Log(message); if (notify && !closing) MessageBox.Show(this, message, "Aster VPN", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { busy = false; OperationProgress.IsIndeterminate = false; OperationProgress.Visibility = Visibility.Collapsed; }
    }
    private async void ConnectClick(object sender, RoutedEventArgs e) => await Run(ConnectSelectedAsync);
    private async Task ConnectSelectedAsync()
    {
        Capture();
        if (selectedServer is not ServerProfile server) throw new InvalidOperationException("Добавьте и выберите сервер.");
        connectedName = server.DisplayName;
        connectedSubscriptionId = settings.Subscriptions.FirstOrDefault(s => s.Servers.Contains(server))?.Id;
        ActiveText.Text = "Подключение: " + server.DisplayName;
        connecting = true;
        SetConnectionAnimation(true);
        try { await engine.ConnectAsync(settings, server); connectedSignature = engine.IsWanted ? Signature(server) : null; }
        finally { connecting = false; SetConnectionAnimation(engine.IsTransitioning); UpdateSelectionDisplay(); }
    }
    private async void DisconnectClick(object sender, RoutedEventArgs e) => await Run(engine.DisconnectAsync);
    private async void UpdateClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if ((sender as FrameworkElement)?.Tag is SubscriptionProfile sub) await UpdateSubscription(sub.Url, sub);
        else
        {
            var address = SubscriptionService.NormalizeAddress(SubscriptionBox.Text);
            await UpdateSubscription(address, settings.Subscriptions.FirstOrDefault(s => s.Url == address));
            SubscriptionBox.Clear();
            SubscriptionPanel.Visibility = Visibility.Collapsed;
        }
    });
    private async Task UpdateSubscription(string address, SubscriptionProfile? existing = null)
    {
        PingText.Text = "Загрузка подписки…";
        var updated = await SubscriptionService.FetchAsync(address);
        if (existing != null)
        {
            updated.Id = existing.Id; updated.IsExpanded = existing.IsExpanded;
            foreach (var profile in updated.Servers)
            {
                var previous = existing.Servers.FirstOrDefault(p => p.Outbound.ToJsonString() == profile.Outbound.ToJsonString()) ?? existing.Servers.FirstOrDefault(p => p.Name == profile.Name);
                if (previous != null) profile.Id = previous.Id;
            }
            settings.Subscriptions[settings.Subscriptions.IndexOf(existing)] = updated;
        }
        else settings.Subscriptions.Add(updated);
        RefreshServers(); SecureStore.Save(settings);
        PingText.Text = $"{updated.Name}: обновлено {updated.Servers.Count} серверов";
        Log(PingText.Text);
    }
    private async void DeleteSubscriptionClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if ((sender as FrameworkElement)?.Tag is not SubscriptionProfile sub) return;
        if (MessageBox.Show(this, $"Удалить «{sub.Name}» и её {sub.Servers.Count} серверов из клиента? Подписка у провайдера сохранится.", "Удалить подписку", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (sub.Id == connectedSubscriptionId)
        {
            await engine.DisconnectAsync(); connectedSignature = connectedName = null;
        }
        settings.Subscriptions.Remove(sub);
        RefreshServers(); SecureStore.Save(settings); PingText.Text = "Подписка удалена";
    });
    private void AddLocalProfiles(List<ServerProfile> profiles)
    {
        var local = settings.Subscriptions.FirstOrDefault(s => s.Url.Length == 0);
        if (local == null) { local = new() { Name = "Локальные профили" }; settings.Subscriptions.Add(local); }
        local.Servers.AddRange(profiles); RefreshServers(); SecureStore.Save(settings);
    }
    private void SetConnectionAnimation(bool active)
    {
        ConnectionRing.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        ConnectionRotation.BeginAnimation(RotateTransform.AngleProperty, active
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }
    private async void ImportClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Профили|*.json;*.txt;*.conf|Все файлы|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        if (new FileInfo(dialog.FileName).Length > 8 * 1024 * 1024) throw new IOException("Файл больше 8 МБ.");
        var profiles = SubscriptionParser.Parse(await File.ReadAllTextAsync(dialog.FileName));
        AddLocalProfiles(profiles); Log($"Импортировано {profiles.Count} серверов.");
    });
    private async void PasteClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var text = SubscriptionService.NormalizeAddress(System.Windows.Clipboard.GetText());
        if (text.Length > 8 * 1024 * 1024) throw new IOException("Данные больше 8 МБ.");
        if (Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme == "https" && url.UserInfo.Length == 0)
        {
            await UpdateSubscription(text, settings.Subscriptions.FirstOrDefault(s => s.Url == text)); return;
        }
        var profiles = SubscriptionParser.Parse(text); AddLocalProfiles(profiles);
        Log($"Импортировано {profiles.Count} серверов.");
    });
    private async void PingClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (selectedServer is not ServerProfile server) throw new InvalidOperationException("Выберите сервер.");
        PingText.Text = "Проверяем…"; var result = await VpnEngine.PingAsync(server);
        server.Latency = result;
        PingText.Text = $"{server.DisplayName}: {result}";
    });
    private async void ProbeClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var ms = await engine.ProbeAsync(); PingText.Text = $"HTTPS через VPN: {ms} мс"; Log(PingText.Text);
        MessageBox.Show(this, $"HTTPS-запрос прошёл через выбранный VPN-сервер за {ms} мс.", "Проверка соединения");
    });
    private async void SaveClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        Capture(); await engine.ApplyOptionsAsync(settings);
        Log("Настройки сохранены. Автозапуск и Kill switch применены; для изменения маршрутизации переподключитесь.");
    });
    private void AddApp(string path)
    {
        path = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new FormatException("Укажите существующий .exe файл.");
        if (!apps.Contains(path, StringComparer.OrdinalIgnoreCase)) apps.Add(path);
    }
    private async void AddPathClick(object sender, RoutedEventArgs e) => await Run(() => { AddApp(AppPathBox.Text); AppPathBox.Clear(); return Task.CompletedTask; });
    private async void BrowseAppClick(object sender, RoutedEventArgs e) => await Run(() =>
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Приложения Windows|*.exe" };
        if (dialog.ShowDialog(this) == true) AddApp(dialog.FileName); return Task.CompletedTask;
    });
    private void RemoveAppClick(object sender, RoutedEventArgs e) { if (AppList.SelectedItem is string path) apps.Remove(path); }
    private async void ProcessesClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var paths = await Task.Run(() =>
        {
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                using (process) { try { if (process.MainModule?.FileName is { } path) items.Add(path); } catch (Win32Exception) { } catch (InvalidOperationException) { } }
            }
            return items.OrderBy(Path.GetFileName).ToList();
        });
        var entries = await Task.Run(() => paths.Select(ProcessPicker.CreateEntry).ToList());
        var picker = new ProcessPicker(entries) { Owner = this };
        if (picker.ShowDialog() == true)
            foreach (var path in picker.SelectedPaths)
                if (File.Exists(path) && !apps.Contains(path, StringComparer.OrdinalIgnoreCase)) apps.Add(path);
    });
    private void Log(string message)
    {
        lock (pendingLogs)
        {
            if (pendingLogs.Count == 256) pendingLogs.Dequeue();
            pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message.Trim()}\n");
        }
    }
    private void FlushLogs()
    {
        string batch;
        lock (pendingLogs)
        {
            if (pendingLogs.Count == 0) return;
            batch = string.Concat(pendingLogs);
            pendingLogs.Clear();
        }
        LogBox.AppendText(batch);
        if (LogBox.Text.Length > 40000) LogBox.Text = LogBox.Text[^30000..];
        if (LogsPage.IsVisible) LogBox.ScrollToEnd();
    }
    internal void ShowWindow() { ShowInTaskbar = true; Show(); WindowState = WindowState.Normal; Activate(); }
    private void RequestExit() { exitRequested = true; Close(); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void NavigationChanged(object sender, RoutedEventArgs e)
    {
        if (Dashboard == null) return;
        var page = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString();
        Dashboard.Visibility = page is "home" or "servers" ? Visibility.Visible : Visibility.Collapsed;
        RoutingPage.Visibility = page == "routes" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = page == "logs" ? Visibility.Visible : Visibility.Collapsed;
        
    }
    private void ShowSubscriptionClick(object sender, RoutedEventArgs e)
    {
        HomeNav.IsChecked = true;
        SubscriptionPanel.Visibility = SubscriptionPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        SubscriptionBox.Focus();
    }
    private void ApplySearch()
    {
        if (SearchBox == null || ListHint == null) return;
        var query = SearchBox.Text.Trim();
        foreach (var list in serverLists)
            CollectionViewSource.GetDefaultView(list.ItemsSource).Filter = item => item is ServerProfile p && (p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.ProtocolDetail.Contains(query, StringComparison.OrdinalIgnoreCase));
        ListHint.Text = settings.AllServers.Count == 0 ? "Добавьте подписку, чтобы увидеть серверы." : "Нажмите название подписки, чтобы свернуть её серверы";
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => ApplySearch();
    private void UpdateSelectionDisplay()
    {
        if (selectedServer is ServerProfile selected)
        {
            SelectedText.Text = selected.DisplayName; SelectedProtocol.Text = selected.ProtocolDetail;
            if (!engine.IsWanted) ActiveText.Text = "Нажмите кнопку для подключения";
            else ActiveText.Text = "Активный: " + connectedName + (connectedSignature != Signature(selected) ? "\nНажмите кнопку, чтобы сменить сервер" : "");
        }
        else { SelectedText.Text = "Сервер не выбран"; SelectedProtocol.Text = "Выберите строку в списке"; }
    }
    private void ServerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedText == null || restoringSelection || sender is not System.Windows.Controls.ListBox list || list.SelectedItem is not ServerProfile selected) return;
        selectedServer = selected; settings.SelectedServerId = selected.Id;
        restoringSelection = true;
        foreach (var other in serverLists) if (other != list) other.SelectedItem = null;
        restoringSelection = false; UpdateSelectionDisplay();
        try { SecureStore.Save(settings); } catch { Log("Не удалось сохранить выбранный сервер."); }
    }
    private void ServerDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, e.OriginalSource as DependencyObject) is ListBoxItem) ConnectClick(sender, e);
    }
    private async void PowerClick(object sender, RoutedEventArgs e)
    {
        if (connecting) { engine.CancelConnection(); ActiveText.Text = "Отмена подключения…"; return; }
        if (engine.IsWanted && (selectedServer is not ServerProfile selected || Signature(selected) == connectedSignature)) await Run(engine.DisconnectAsync);
        else ConnectClick(sender, e);
    }
    private async void PingAllClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        using var parallel = new SemaphoreSlim(4);
        PingText.Text = "Проверка задержки серверов…";
        await Task.WhenAll((((sender as FrameworkElement)?.Tag as SubscriptionProfile)?.Servers ?? settings.AllServers).ToArray().Select(async server =>
        {
            await parallel.WaitAsync();
            try
            {
                var result = await VpnEngine.PingAsync(server);
                server.Latency = result;
            }
            finally { parallel.Release(); }
        }));
        PingText.Text = "Проверка завершена: TCP / ICMP. ICMP проверяет узел, а не VPN-туннель.";
    });
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closed) return; e.Cancel = true; if (closing) return;
        if (!exitRequested) { Hide(); return; }
        closing = true; engine.CancelConnection(); updateTimer.Stop(); logTimer.Stop();
        try { await engine.DisposeAsync(); }
        catch (Exception ex) { MessageBox.Show(this, "Не удалось снять блокировку Kill switch. Запустите клиент снова и отключите Kill switch в настройках.\n" + ex.Message, "Aster VPN"); }
        finally { tray.Dispose(); applicationIcon.Dispose(); closed = true; Close(); }
    }
}
