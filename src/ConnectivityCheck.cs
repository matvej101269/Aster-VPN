namespace AsterVPN;

public sealed record ConnectivityResult(long ElapsedMilliseconds, int Attempts);

public static class ConnectivityCheck
{
    // A single blocked/overloaded website must not tear down every application session.
    public static async Task<ConnectivityResult> RunAsync(string primary, Func<Uri, CancellationToken, Task> probe, CancellationToken ct,
        TimeSpan? fallbackDelay = null, TimeSpan? probeTimeout = null)
    {
        if (!Uri.TryCreate(primary, UriKind.Absolute, out var first) || first.Scheme != "https")
            throw new FormatException("Адрес проверки должен начинаться с HTTPS.");
        ct.ThrowIfCancellationRequested();
        using var finished = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var targets = new[] { first, new Uri("https://www.gstatic.com/generate_204"), new Uri("https://www.cloudflare.com/cdn-cgi/trace") }.DistinctBy(u => u.Host).ToArray();
        var pending = new List<Task<(long Milliseconds, Exception? Error)>>();
        var attempts = 0;
        Exception? last = null;
        async Task<(long, Exception?)> RunProbe(Uri uri)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(finished.Token);
            budget.CancelAfter(probeTimeout ?? TimeSpan.FromSeconds(12));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { await probe(uri, budget.Token).WaitAsync(budget.Token); return (watch.ElapsedMilliseconds, null); }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or OperationCanceledException)
            {
                return (0, ex);
            }
        }
        // Observe late completion even when a third-party probe ignores cancellation.
        static async Task Observe(Task task) { try { await task; } catch { } }
        void Launch() => pending.Add(RunProbe(targets[attempts++]));
        try
        {
            Launch();
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var next = attempts < targets.Length ? Task.Delay(fallbackDelay ?? TimeSpan.FromSeconds(2), finished.Token) : null;
                var done = await Task.WhenAny(next == null ? pending.Cast<Task>() : pending.Cast<Task>().Append(next));
                ct.ThrowIfCancellationRequested();
                if (done == next) { Launch(); continue; }
                var completed = (Task<(long Milliseconds, Exception? Error)>)done;
                pending.Remove(completed);
                var result = await completed;
                if (result.Error == null) return new(result.Milliseconds, attempts);
                last = result.Error;
                if (pending.Count == 0 && attempts < targets.Length) Launch();
            }
            throw new System.IO.IOException("Все адреса проверки недоступны через TUN.", last);
        }
        finally
        {
            finished.Cancel();
            foreach (var task in pending) _ = Observe(task);
        }
    }
}
