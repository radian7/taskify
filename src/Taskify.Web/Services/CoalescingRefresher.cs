namespace Taskify.Web.Services;

/// <summary>
/// Runs a re-fetch at most once per window for one open screen (research R5). The first signal re-fetches at once;
/// signals that arrive while the window is open are merged into one trailing re-fetch when it closes. This keeps a
/// busy board inside the 300 reads per minute limit of a viewer.
/// </summary>
public sealed class CoalescingRefresher : IDisposable
{
    /// <summary>The default window: one re-fetch per second.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(1);

    private readonly Func<Task> refresh;
    private readonly TimeProvider time;
    private readonly TimeSpan window;
    private readonly Action<Exception>? onError;
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private bool active;
    private bool pending;
    private bool disposed;

    /// <summary>Creates a refresher.</summary>
    /// <param name="refresh">Re-fetches the data and redraws the screen. It should handle its own errors.</param>
    /// <param name="time">The clock used for the window.</param>
    /// <param name="window">The window; <see cref="DefaultWindow"/> when <see langword="null"/>.</param>
    /// <param name="onError">Called when <paramref name="refresh"/> throws; the error never escapes.</param>
    public CoalescingRefresher(Func<Task> refresh, TimeProvider time, TimeSpan? window = null, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(time);
        this.refresh = refresh;
        this.time = time;
        this.window = window ?? DefaultWindow;
        this.onError = onError;
    }

    /// <summary>Asks for a re-fetch. Returns at once; the re-fetch runs in the background.</summary>
    public void Signal()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (active)
            {
                pending = true;   // merged into the trailing re-fetch
                return;
            }

            active = true;
        }

        _ = RunAsync();
    }

    /// <summary>Stops the refresher; a pending re-fetch is dropped.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pending = false;
        }

        stop.Cancel();
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                try
                {
                    await refresh();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A failed re-fetch must not take the circuit down; the next signal tries again.
                    onError?.Invoke(exception);
                }

                await Task.Delay(window, time, stop.Token);

                lock (gate)
                {
                    if (!pending || disposed)
                    {
                        active = false;
                        return;
                    }

                    pending = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (gate)
            {
                active = false;
            }
        }
    }
}
