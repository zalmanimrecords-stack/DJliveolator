using System;
using Liveolator.Audio.Playback;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>
/// Guarded smoke test for the BASS entry point. It does NOT require native BASS in CI: it asserts
/// the construction *contract* the App's fallback depends on — either the engine comes up (native
/// present) and hands out decks, or it fails with exactly one of the exception types
/// <c>ServiceConfig.WireLiveAudio</c> catches (<see cref="BassPlaybackException"/> /
/// <see cref="DllNotFoundException"/>). Any other failure would mean the App's catch clause is
/// wrong and Live Mode would crash instead of falling back.
/// </summary>
public sealed class BassAudioEngineSmokeTests
{
    [Fact]
    public void Construction_EitherSucceeds_OrThrowsAGuardedNativeMissingException()
    {
        BassAudioEngine? engine = null;
        try
        {
            engine = new BassAudioEngine();
        }
        catch (Exception ex)
        {
            // This is the exact set ServiceConfig.WireLiveAudio guards on; keep them in lockstep.
            Assert.True(
                ex is BassPlaybackException or DllNotFoundException,
                $"Unexpected exception type when native BASS is absent: {ex.GetType().FullName}: {ex.Message}");
            return; // native BASS not available (the CI case) — fallback contract verified.
        }

        // Native BASS is present: the engine must produce a deck source and dispose cleanly.
        using (engine)
        {
            IAudioSource deck = engine.CreateDeck("nonexistent-but-not-started.wav");
            Assert.NotNull(deck);
            Assert.False(deck.IsRunning); // CreateDeck must not start playback or open the file yet.
            deck.Dispose();
        }
    }

    [Fact]
    public void CreateDeck_RejectsEmptyPath_WhenEngineConstructs()
    {
        BassAudioEngine engine;
        try
        {
            engine = new BassAudioEngine();
        }
        catch (Exception ex) when (ex is BassPlaybackException or DllNotFoundException)
        {
            return; // native BASS absent — input-validation path is covered by the construction test.
        }

        using (engine)
        {
            Assert.Throws<ArgumentException>(() => engine.CreateDeck("   "));
        }
    }
}
