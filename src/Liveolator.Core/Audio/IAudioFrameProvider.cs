namespace Liveolator.Core.Audio;

/// <summary>
/// Frame pipeline seam (doc 02): turns raw <see cref="IAudioSource"/> samples into analysis
/// frames. The beat engine (doc 03) and visuals subscribe to <see cref="FrameAvailable"/> and
/// share the <em>same</em> frames, guaranteeing beat analysis and visuals see identical audio.
/// </summary>
public interface IAudioFrameProvider
{
    /// <summary>The most recent frame, or <see cref="AudioFrameData.Empty"/> if none yet.</summary>
    AudioFrameData GetLatestFrame();

    /// <summary>Raised once per analysis frame as audio flows through the pipeline.</summary>
    event EventHandler<AudioFrameData>? FrameAvailable;
}
