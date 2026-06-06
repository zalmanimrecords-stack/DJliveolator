using Liveolator.Core.Audio;
using Liveolator.Core.Settings;

namespace Liveolator.Audio.Playback;

/// <summary>
/// Native <see cref="IAudioEngineReinitializer"/> over <see cref="TwoDeckBassEngine"/>: applies a
/// runtime output-device / buffer change by re-opening BASS (doc 12 Settings). The decision + rollback
/// logic is the pure <see cref="AudioReinitCoordinator"/> in Core; this thin adapter just forwards to the
/// engine's <see cref="TwoDeckBassEngine.ReinitializeOutput"/>, which touches native BASS and is verified
/// manually (native BASS is absent in CI), mirroring the rest of the audio binding.
/// </summary>
public sealed class BassAudioEngineReinitializer : IAudioEngineReinitializer
{
    private readonly TwoDeckBassEngine _engine;

    public BassAudioEngineReinitializer(TwoDeckBassEngine engine)
        => _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public bool Reinitialize(AudioSettings settings) => _engine.ReinitializeOutput(settings);
}
