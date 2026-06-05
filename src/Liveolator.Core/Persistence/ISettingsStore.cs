using Liveolator.Core.Settings;

namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists the application <see cref="AppSettings"/> (audio output + MIDI choices) across runs
/// (doc 12/13). Implemented in <c>Liveolator.Media</c> as a single JSON file under the per-user
/// Liveolator folder; the seam lives in Core so the Settings UI depends only on the abstraction (Core
/// iron rule #3) and is unit-testable with a fake.
/// </summary>
/// <remarks>
/// Loads are tolerant: a missing, unreadable, or incompatible-version file yields
/// <see cref="AppSettings.Default"/> and a warning, never an exception (global standards #16/#26).
/// Saves are atomic (temp-then-move), mirroring the other stores.
/// </remarks>
public interface ISettingsStore
{
    /// <summary>Loads the saved settings, or <see cref="AppSettings.Default"/> when none/unreadable.</summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves (creates or replaces) the settings.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
