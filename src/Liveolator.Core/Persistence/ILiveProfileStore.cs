using Liveolator.Core.Autopilot;
using Liveolator.Core.Mapping;
using Liveolator.Core.Visuals;

namespace Liveolator.Core.Persistence;

/// <summary>
/// Persists the authored Live-Mode data — controller mapping profiles, visual banks, the macro
/// definitions, and autopilot rule-sets — to/from the per-user <c>live/</c> layout (doc 13).
/// Implemented in <c>Liveolator.Media</c>; the seam lives in Core so engines/UI depend only on the
/// abstraction (Core iron rule #3).
/// </summary>
/// <remarks>
/// Loads are tolerant: a missing, unreadable, or older-schema file yields <c>null</c>/empty and a
/// warning, never an exception (global standards #16/#26). Saves are atomic (temp-then-move).
/// </remarks>
public interface ILiveProfileStore
{
    /// <summary>Saves a controller mapping profile under <c>live/mappings/&lt;name&gt;.json</c>.</summary>
    Task SaveMappingProfileAsync(ControllerMappingProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the named controller mapping profile, or <c>null</c> when it is missing, unreadable, or
    /// written by an incompatible schema version.
    /// </summary>
    Task<ControllerMappingProfile?> LoadMappingProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Saves a visual bank (and its scenes) under <c>live/scenes/&lt;name&gt;.json</c>.</summary>
    Task SaveVisualBankAsync(VisualBank bank, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the named visual bank, or <c>null</c> when it is missing, unreadable, or written by an
    /// incompatible schema version.
    /// </summary>
    Task<VisualBank?> LoadVisualBankAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the names of all saved visual banks (the file names under <c>live/scenes/</c>, without the
    /// extension), ordered case-insensitively. Returns an empty list when none exist or the folder is
    /// unreadable — never throws (global standards #16/#26). Drives the runtime bank picker (doc 22 C3).
    /// </summary>
    Task<IReadOnlyList<string>> ListVisualBankNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the macro definitions under <c>live/macros.json</c>.</summary>
    Task SaveVisualMacrosAsync(IEnumerable<VisualMacro> macros, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the macro definitions, or an empty list when none exist, the file is unreadable, or it
    /// was written by an incompatible schema version.
    /// </summary>
    Task<IReadOnlyList<VisualMacro>> LoadVisualMacrosAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves an autopilot rule-set under <c>live/autopilot/&lt;name&gt;.json</c>.</summary>
    Task SaveAutopilotRuleSetAsync(AutopilotRuleSet ruleSet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the named autopilot rule-set, or <c>null</c> when it is missing, unreadable, or written
    /// by an incompatible schema version.
    /// </summary>
    Task<AutopilotRuleSet?> LoadAutopilotRuleSetAsync(string name, CancellationToken cancellationToken = default);
}
