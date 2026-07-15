namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Trailing debounce: the callback runs one <paramref name="delay"/> after the first
/// <see cref="Signal"/>, coalescing every signal that arrives in between, and never more than once
/// per delay window. Callbacks read the latest state at fire time, so the coalesced value is always
/// current. Callback exceptions are swallowed — the callback owns its error handling.
/// </summary>
internal sealed class Debouncer(TimeSpan delay, Func<Task> callback) : IDisposable
{
    private long _signals;
    private long _handled;
    private int _running;
    private volatile bool _disposed;

    public void Signal()
    {
        if (_disposed)
            return;
        Interlocked.Increment(ref _signals);
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        try
        {
            while (!_disposed && Interlocked.Read(ref _handled) != Interlocked.Read(ref _signals))
            {
                await Task.Delay(delay);
                Interlocked.Exchange(ref _handled, Interlocked.Read(ref _signals));
                if (_disposed)
                    return;

                try
                {
                    await callback();
                }
                catch
                {
                    // The callback owns its error handling.
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            // A signal that raced the loop exit would otherwise be dropped — re-arm for it.
            if (!_disposed && Interlocked.Read(ref _handled) != Interlocked.Read(ref _signals))
                Signal();
        }
    }

    public void Dispose() => _disposed = true;
}
