using System.Threading;

namespace Liveolator.App.Composition;

/// <summary>
/// Process-wide guard that lets only the first Liveolator instance run. A second launch finds the
/// named mutex already held and learns it is <b>not</b> primary, so it can exit instead of opening a
/// duplicate window. Concurrent instances share one <c>%APPDATA%</c> state (settings, live set, and the
/// FRKTL shader cache); overlapping them caused presets to vanish when a second instance could not
/// rewrite a cache <c>.frag</c> the first still held open. Single-instance removes that whole class of bug.
/// </summary>
/// <remarks>
/// The mutex name is stable and per-user (not <c>Global\</c>), which is the right scope for a desktop
/// app: one instance per logged-in user, while a second user session can still run its own. Owns the
/// mutex for the process lifetime; <see cref="Dispose"/> releases it so the next launch becomes primary.
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>The default per-user mutex name for the app.</summary>
    public const string DefaultName = "Liveolator.App.SingleInstance";

    private readonly Mutex? _mutex;

    public SingleInstanceGuard(string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            _mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
            IsPrimary = createdNew;
        }
        catch (Exception ex) when (ex is AbandonedMutexException)
        {
            // A prior primary died without releasing; we now own it and are the primary.
            IsPrimary = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // The platform denied the named mutex (rare). Degrade to "primary" rather than blocking
            // startup — a missing guard is better than refusing to launch at all.
            _mutex = null;
            IsPrimary = true;
        }
    }

    /// <summary>True when this process is the first/only instance and should run normally.</summary>
    public bool IsPrimary { get; }

    public void Dispose()
    {
        if (_mutex is null)
            return;
        try
        {
            if (IsPrimary)
                _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread / already released — nothing to do.
        }
        _mutex.Dispose();
    }
}
