using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
namespace AsterVPN;
public partial class App : System.Windows.Application
{
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    private Mutex? instance;
    private InstanceActivation? activation;
    protected override void OnStartup(StartupEventArgs e)
    {
        activation = new InstanceActivation("Local\\AsterVPN.Activate");
        instance = new Mutex(true, "Local\\AsterVPN.Desktop", out var first);
        if (!first)
        {
            using var current = Process.GetCurrentProcess();
            foreach (var other in Process.GetProcessesByName(current.ProcessName))
                using (other)
                    try { if (other.Id != current.Id && other.SessionId == current.SessionId) AllowSetForegroundWindow(other.Id); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
            activation.Signal(); Shutdown(); return;
        }
        base.OnStartup(e);
        var window = new MainWindow(e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase));
        MainWindow = window;
        window.Show();
        activation.Listen(() =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.InvokeAsync(() => { if (!Dispatcher.HasShutdownStarted) window.ShowWindow(); });
        });
    }
    protected override void OnExit(ExitEventArgs e)
    {
        activation?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
