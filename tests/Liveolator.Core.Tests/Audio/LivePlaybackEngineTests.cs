using System;
using System.Collections.Generic;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Tests.Beat;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class LivePlaybackEngineTests
{
    /// <summary>Hands out pre-made fake decks so a test can drive their samples.</summary>
    private sealed class FakeDeckSourceFactory : IDeckSourceFactory
    {
        private readonly Queue<FakeAudioSource> _decks = new();
        public List<FakeAudioSource> Created { get; } = new();

        public FakeAudioSource Enqueue()
        {
            var deck = new FakeAudioSource();
            _decks.Enqueue(deck);
            return deck;
        }

        public IAudioSource CreateDeck(string filePath)
        {
            FakeAudioSource deck = _decks.Count > 0 ? _decks.Dequeue() : new FakeAudioSource();
            Created.Add(deck);
            return deck;
        }
    }

    private static LivePlaybackEngine NewEngine(FakeDeckSourceFactory factory)
        => new(factory, new FakeHostClock(1_000_000));

    [Fact]
    public void Load_CreatesDeckViaFactory()
    {
        var factory = new FakeDeckSourceFactory();
        using var engine = NewEngine(factory);

        engine.Load(@"C:\a.wav");

        Assert.Single(factory.Created);
    }

    [Fact]
    public void SecondLoad_DisposesPreviousDeck()
    {
        var factory = new FakeDeckSourceFactory();
        FakeAudioSource first = factory.Enqueue();
        using var engine = NewEngine(factory);

        engine.Load(@"C:\a.wav");
        engine.Load(@"C:\b.wav");

        Assert.Equal(1, first.DisposeCount);
    }

    [Fact]
    public void PlayPause_TogglesDeckAndIsPlaying()
    {
        var factory = new FakeDeckSourceFactory();
        using var engine = NewEngine(factory);
        engine.Load(@"C:\a.wav");

        Assert.False(engine.IsPlaying);
        engine.PlayPause();
        Assert.True(engine.IsPlaying);
        engine.PlayPause();
        Assert.False(engine.IsPlaying);
    }

    [Fact]
    public void PlayPause_WithNothingLoaded_IsNoOp()
    {
        var factory = new FakeDeckSourceFactory();
        using var engine = NewEngine(factory);

        engine.PlayPause();

        Assert.False(engine.IsPlaying);
    }

    [Fact]
    public void Stop_StopsDeck()
    {
        var factory = new FakeDeckSourceFactory();
        using var engine = NewEngine(factory);
        engine.Load(@"C:\a.wav");
        engine.PlayPause();

        engine.Stop();

        Assert.False(engine.IsPlaying);
    }

    [Fact]
    public void BeatClock_IsStableAcrossLoads()
    {
        var factory = new FakeDeckSourceFactory();
        using var engine = NewEngine(factory);

        engine.Load(@"C:\a.wav");
        IBeatClock clock1 = engine.BeatClock;
        engine.Load(@"C:\b.wav");
        IBeatClock clock2 = engine.BeatClock;

        Assert.Same(clock1, clock2);
    }

    [Fact]
    public void Load_EmptyPath_Throws()
    {
        var factory = new FakeDeckSourceFactory();
        using var engine = NewEngine(factory);

        Assert.Throws<ArgumentException>(() => engine.Load("  "));
    }

    [Fact]
    public void DetectsTempo_EndToEnd_ThroughDeckPipelineAndClock()
    {
        // Full Core audio chain: a click-track deck -> frame pipeline -> beat clock.
        var factory = new FakeDeckSourceFactory();
        FakeAudioSource deck = factory.Enqueue();
        using var engine = NewEngine(factory);
        engine.Load(@"C:\click.wav");

        const int rate = 44_100;
        const int period = 22_050;   // impulse every 0.5 s -> 120 BPM
        const int seconds = 12;
        int total = rate * seconds;

        // Feed the click track mono, in 0.1 s chunks.
        const int chunk = rate / 10;
        var buffer = new float[chunk];
        for (int start = 0; start < total; start += chunk)
        {
            Array.Clear(buffer);
            for (int i = 0; i < chunk; i++)
            {
                int globalIndex = start + i;
                if (globalIndex % period == 0)
                    buffer[i] = 1.0f; // onset impulse
            }
            deck.Emit((float[])buffer.Clone(), channels: 1, sampleRate: rate);
        }

        Assert.InRange(engine.BeatClock.Current.Bpm, 105.0, 135.0);
    }

    [Fact]
    public void Dispose_DisposesDeck()
    {
        var factory = new FakeDeckSourceFactory();
        FakeAudioSource deck = factory.Enqueue();
        var engine = NewEngine(factory);
        engine.Load(@"C:\a.wav");

        engine.Dispose();

        Assert.Equal(1, deck.DisposeCount);
    }
}
