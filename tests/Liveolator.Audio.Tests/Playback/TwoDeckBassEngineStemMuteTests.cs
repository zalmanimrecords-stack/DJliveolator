using System.Collections.Generic;
using System.Linq;
using Liveolator.Audio.Playback;
using Liveolator.Core.Analysis.Stems;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// Per-stem mute on a 4-stem submix deck (doc 32 §2b, slice 2). Drives the real engine over the fake
/// backend, so the MANAGED state (per-track mute, reset-on-load, single-file no-op, backend routing) is
/// exercised; the click-free native volume ramp itself is owner-verified on hardware, never in CI.
/// </summary>
public sealed class TwoDeckBassEngineStemMuteTests
{
    private const string Track = @"S:\music\track.flac";

    private static StemSet LocalStems()
        => new(Track, "umxhq", new Dictionary<StemKind, string>
        {
            [StemKind.Drums] = @"C:\cache\drums.flac",
            [StemKind.Bass] = @"C:\cache\bass.flac",
            [StemKind.Vocals] = @"C:\cache\vocals.flac",
            [StemKind.Other] = @"C:\cache\other.flac",
        });

    private sealed class FakeStemCache : IStemCache
    {
        private readonly StemSet? _set;
        public FakeStemCache(StemSet? set) => _set = set;
        public StemSet? TryLoad(string sourcePath) => _set;
    }

    private static TwoDeckBassEngine Build(FakeBassMixerBackend backend, IStemCache? cache, bool stemsEnabled)
        => new(backend, new BassMixer(), loggerFactory: null,
               hotCueStore: null, phaseLock: null, stemCache: cache, stemsEnabled: stemsEnabled);

    [Fact]
    public void StemMute_OnStemDeck_RampsBackendVolumeAndTracksState()
    {
        var backend = new FakeBassMixerBackend();
        using TwoDeckBassEngine engine = Build(backend, new FakeStemCache(LocalStems()), stemsEnabled: true);
        engine.Load(0, Track);
        int handle = backend.StemDecks.Keys.Single();

        engine.SetStemMuted(0, StemKind.Bass, muted: true);

        Assert.True(engine.IsStemDeck(0));
        Assert.True(engine.IsStemMuted(0, StemKind.Bass));
        Assert.False(engine.IsStemMuted(0, StemKind.Drums)); // other stems untouched
        // Muting a stem disables (enabled:false) exactly that decoder on the loaded submix handle.
        Assert.Contains((handle, StemKind.Bass, false), backend.StemEnableCalls);

        engine.SetStemMuted(0, StemKind.Bass, muted: false);
        Assert.False(engine.IsStemMuted(0, StemKind.Bass));
        Assert.Contains((handle, StemKind.Bass, true), backend.StemEnableCalls);
    }

    [Fact]
    public void StemMute_OnSingleFileDeck_IsNoOp()
    {
        var backend = new FakeBassMixerBackend();
        using TwoDeckBassEngine engine = Build(backend, new FakeStemCache(set: null), stemsEnabled: true);
        engine.Load(0, Track); // no cached stems → single-file deck

        engine.SetStemMuted(0, StemKind.Vocals, muted: true);

        Assert.False(engine.IsStemDeck(0));
        Assert.False(engine.IsStemMuted(0, StemKind.Vocals)); // state never recorded for a non-stem deck
        Assert.Empty(backend.StemEnableCalls);                 // and the backend is never touched
    }

    [Fact]
    public void Load_ResetsStemMuteToAudible()
    {
        var backend = new FakeBassMixerBackend();
        using TwoDeckBassEngine engine = Build(backend, new FakeStemCache(LocalStems()), stemsEnabled: true);
        engine.Load(0, Track);
        engine.SetStemMuted(0, StemKind.Drums, muted: true);
        Assert.True(engine.IsStemMuted(0, StemKind.Drums));

        engine.Load(0, Track); // fresh decoders open at unity — mute is per-track

        Assert.False(engine.IsStemMuted(0, StemKind.Drums));
    }
}
