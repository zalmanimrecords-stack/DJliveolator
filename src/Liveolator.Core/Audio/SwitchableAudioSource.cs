namespace Liveolator.Core.Audio;

/// <summary>
/// An <see cref="IAudioSource"/> that transparently forwards a swappable inner source's samples.
/// Lets a stable frame pipeline + beat clock stay subscribed while the underlying deck changes per
/// track, so loading a new track never breaks the clock's subscription. Pure (no native) and
/// thread-safe on source swaps.
/// </summary>
public sealed class SwitchableAudioSource : IAudioSource
{
    private readonly object _gate = new();
    private IAudioSource? _inner;

    public event EventHandler<AudioSamplesAvailable>? SamplesAvailable;

    public string Name
    {
        get { lock (_gate) return _inner?.Name ?? "(none)"; }
    }

    public bool IsRunning
    {
        get { lock (_gate) return _inner?.IsRunning ?? false; }
    }

    /// <summary>
    /// Set the inner source (or null to detach). Ownership of <paramref name="source"/> stays with
    /// the caller — this type wires events only and never disposes the inner source.
    /// </summary>
    public void SetSource(IAudioSource? source)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_inner, source))
                return;
            if (_inner is not null)
                _inner.SamplesAvailable -= Forward;
            _inner = source;
            if (_inner is not null)
                _inner.SamplesAvailable += Forward;
        }
    }

    private void Forward(object? sender, AudioSamplesAvailable e) => SamplesAvailable?.Invoke(this, e);

    public void Start()
    {
        IAudioSource? inner;
        lock (_gate) inner = _inner;
        inner?.Start();
    }

    public void Stop()
    {
        IAudioSource? inner;
        lock (_gate) inner = _inner;
        inner?.Stop();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_inner is not null)
                _inner.SamplesAvailable -= Forward;
            _inner = null;
        }
    }
}
