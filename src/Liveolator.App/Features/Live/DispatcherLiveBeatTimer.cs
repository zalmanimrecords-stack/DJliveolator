using Avalonia.Threading;

namespace Liveolator.App.Features.Live;

/// <summary>
/// Production <see cref="ILiveBeatTimer"/> backed by Avalonia's UI-thread <see cref="DispatcherTimer"/>.
/// Ticks at roughly 60 Hz so the beat/bar phase and pulse indicator animate smoothly; the tick only
/// re-publishes clock state (cheap), so a render-rate cadence is appropriate.
/// </summary>
public sealed class DispatcherLiveBeatTimer : ILiveBeatTimer
{
    // ~60 Hz: smooth pulse animation without burning the UI thread.
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(16);

    private readonly DispatcherTimer _timer;

    public DispatcherLiveBeatTimer()
    {
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? Tick;

    /// <inheritdoc />
    public void Start() => _timer.Start();

    /// <inheritdoc />
    public void Stop() => _timer.Stop();
}
