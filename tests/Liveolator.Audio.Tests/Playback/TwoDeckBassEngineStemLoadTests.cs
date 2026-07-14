using System.Collections.Generic;
using Liveolator.Audio.Playback;
using Liveolator.Core.Analysis.Stems;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// The deck Load branch that chooses a 4-stem submix vs the single file (doc 32 §2b). Drives the real
/// engine state machine over the fake backend, so only the MANAGED decision + fallback is exercised
/// (the native submix playback is owner-verified on hardware, never in CI).
/// </summary>
public sealed class TwoDeckBassEngineStemLoadTests
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
        public int Lookups { get; private set; }
        public StemSet? TryLoad(string sourcePath) { Lookups++; return _set; }
    }

    private static TwoDeckBassEngine Build(
        FakeBassMixerBackend backend, IStemCache? cache, bool stemsEnabled)
        => new(backend, new BassMixer(), loggerFactory: null,
               hotCueStore: null, phaseLock: null, stemCache: cache, stemsEnabled: stemsEnabled);

    [Fact]
    public void GateOff_LoadsSingleFile_EvenWithCachedStems()
    {
        var backend = new FakeBassMixerBackend();
        using TwoDeckBassEngine engine = Build(backend, new FakeStemCache(LocalStems()), stemsEnabled: false);

        engine.Load(0, Track);

        Assert.Equal(new[] { Track }, backend.Opened); // single-file path
        Assert.Empty(backend.StemDecks);
    }

    [Fact]
    public void GateOn_NoCachedStems_LoadsSingleFile()
    {
        var backend = new FakeBassMixerBackend();
        using TwoDeckBassEngine engine = Build(backend, new FakeStemCache(set: null), stemsEnabled: true);

        engine.Load(0, Track);

        Assert.Equal(new[] { Track }, backend.Opened);
        Assert.Empty(backend.StemDecks);
    }

    [Fact]
    public void GateOn_NetworkStemPath_LoadsSingleFile()
    {
        var onNetwork = new StemSet(Track, "umxhq", new Dictionary<StemKind, string>
        {
            [StemKind.Drums] = @"\\nas\stems\drums.flac",
            [StemKind.Bass] = @"\\nas\stems\bass.flac",
            [StemKind.Vocals] = @"\\nas\stems\vocals.flac",
            [StemKind.Other] = @"\\nas\stems\other.flac",
        });
        var backend = new FakeBassMixerBackend();
        using TwoDeckBassEngine engine = Build(backend, new FakeStemCache(onNetwork), stemsEnabled: true);

        engine.Load(0, Track);

        Assert.Equal(new[] { Track }, backend.Opened);
        Assert.Empty(backend.StemDecks);
    }

    [Fact]
    public void GateOn_CompleteLocalStems_OpensStemDeck()
    {
        var backend = new FakeBassMixerBackend();
        using TwoDeckBassEngine engine = Build(backend, new FakeStemCache(LocalStems()), stemsEnabled: true);

        engine.Load(0, Track);

        Assert.Empty(backend.Opened);          // NOT the single-file path
        Assert.Single(backend.StemDecks);      // a stem deck was opened
    }

    [Fact]
    public void GateOn_StemOpenFails_FallsBackToSingleFileOnce()
    {
        var backend = new FakeBassMixerBackend { FailStemOpen = true };
        using TwoDeckBassEngine engine = Build(backend, new FakeStemCache(LocalStems()), stemsEnabled: true);

        engine.Load(0, Track); // must not throw — a corrupt stem never empties a deck

        Assert.Equal(new[] { Track }, backend.Opened); // exactly one fallback open
        Assert.Empty(backend.StemDecks);               // the stem deck open was abandoned
        Assert.True(engine.IsPlaying(0) == false);      // loaded (paused), deck intact
    }

    [Fact]
    public void GateOff_NeverConsultsTheCache()
    {
        var backend = new FakeBassMixerBackend();
        var cache = new FakeStemCache(LocalStems());
        using TwoDeckBassEngine engine = Build(backend, cache, stemsEnabled: false);

        engine.Load(0, Track);

        Assert.Equal(0, cache.Lookups); // gate short-circuits before any cache IO
    }
}
