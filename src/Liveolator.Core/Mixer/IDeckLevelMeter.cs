namespace Liveolator.Core.Mixer;

/// <summary>Read-only realtime level snapshots for the mixer UI.</summary>
public interface IDeckLevelMeter
{
    DeckLevel GetLevel(int slot);
}
