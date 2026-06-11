using Liveolator.Core.Mixer;

namespace Liveolator.Core.Automix;

/// <summary>
/// The auto-mix engine's read-only window onto the decks and the mixer. Reading goes through this
/// seam; every WRITE goes through <c>PerformanceAction</c>s like any other input source (doc 04) —
/// auto-mix is automation of the same controls a human uses, never a back door into the engine.
/// </summary>
public interface IAutomixDeckReader
{
    /// <summary>Snapshot one deck slot (A = 0, B = 1).</summary>
    AutomixDeckSnapshot ReadDeck(int slot);

    /// <summary>The authoritative mixer state (crossfader, per-channel EQ/filter) for snapshots/restores.</summary>
    MixerState Mixer { get; }
}
