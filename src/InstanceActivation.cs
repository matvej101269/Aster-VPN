using System.Threading;
namespace AsterVPN;

// Auto-reset retains a second launch even while the first window is still starting.
public sealed class InstanceActivation : IDisposable
{
    private readonly EventWaitHandle signal;
    private RegisteredWaitHandle? listener;
    public InstanceActivation(string name) => signal = new EventWaitHandle(false, EventResetMode.AutoReset, name);
    public void Signal() => signal.Set();
    public void Listen(Action activate) => listener = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => activate(), null, Timeout.Infinite, false);
    public void Dispose() { listener?.Unregister(null); signal.Dispose(); }
}
