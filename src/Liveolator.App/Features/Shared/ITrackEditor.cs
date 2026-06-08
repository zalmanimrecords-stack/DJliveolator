using Liveolator.Core.Library.Music;

namespace Liveolator.App.Features.Shared;

public interface ITrackEditor
{
    Task<TrackEditResult?> EditAsync(MusicTrack track);
}

public sealed record TrackEditResult(double Bpm, string Camelot, string? Genre, string? Notes);
