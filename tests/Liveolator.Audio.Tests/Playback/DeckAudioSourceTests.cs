using System;
using System.Collections.Generic;
using Liveolator.Audio.Playback;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Audio.Tests.Playback;

public class DeckAudioSourceTests
{
    private static DeckAudioSource NewDeck(FakeBassPlayback bass, string path = @"C:\music\track.wav")
        => new(bass, path, NullLogger<DeckAudioSource>.Instance);

    [Fact]
    public void Name_IsFileName()
    {
        var deck = NewDeck(new FakeBassPlayback(), @"C:\music\set\opener.flac");
        Assert.Equal("opener.flac", deck.Name);
    }

    [Fact]
    public void Start_LoadsStreamGetsInfoArmsTapAndPlays()
    {
        var bass = new FakeBassPlayback();
        var deck = NewDeck(bass);

        deck.Start();

        Assert.True(deck.IsRunning);
        Assert.Equal(1, bass.CreateStreamCalls);
        Assert.Equal(1, bass.PlayCalls);
    }

    [Fact]
    public void Start_IsIdempotent_DoesNotReloadStream()
    {
        var bass = new FakeBassPlayback();
        var deck = NewDeck(bass);

        deck.Start();
        deck.Start();

        Assert.Equal(1, bass.CreateStreamCalls);
        Assert.Equal(1, bass.PlayCalls);
    }

    [Fact]
    public void Restart_AfterStop_ReusesStreamAndPlaysAgain()
    {
        var bass = new FakeBassPlayback();
        var deck = NewDeck(bass);

        deck.Start();
        deck.Stop();
        deck.Start();

        Assert.True(deck.IsRunning);
        Assert.Equal(1, bass.CreateStreamCalls); // stream kept across stop
        Assert.Equal(2, bass.PlayCalls);
    }

    [Fact]
    public void Stop_PausesAndIsIdempotent()
    {
        var bass = new FakeBassPlayback();
        var deck = NewDeck(bass);

        deck.Start();
        deck.Stop();
        deck.Stop();

        Assert.False(deck.IsRunning);
        Assert.Equal(1, bass.PauseCalls);
    }

    [Fact]
    public void TappedSamples_AreForwardedWithChannelFormat()
    {
        var bass = new FakeBassPlayback { Info = new BassChannelInfo(2, 44_100) };
        var deck = NewDeck(bass);
        var received = new List<AudioSamplesAvailable>();
        deck.SamplesAvailable += (_, e) => received.Add(e);

        deck.Start();
        var buffer = new float[] { 0.1f, -0.1f, 0.2f, -0.2f };
        bass.EmitSamples(buffer);

        Assert.Single(received);
        Assert.Equal(2, received[0].Channels);
        Assert.Equal(44_100, received[0].SampleRate);
        Assert.True(buffer.AsSpan().SequenceEqual(received[0].Interleaved.Span));
    }

    [Fact]
    public void EmptyTap_RaisesNothing()
    {
        var bass = new FakeBassPlayback();
        var deck = NewDeck(bass);
        var received = new List<AudioSamplesAvailable>();
        deck.SamplesAvailable += (_, e) => received.Add(e);

        deck.Start();
        bass.EmitSamples(Array.Empty<float>());

        Assert.Empty(received);
    }

    [Fact]
    public void Dispose_FreesTheStream()
    {
        var bass = new FakeBassPlayback { HandleToReturn = 7 };
        var deck = NewDeck(bass);

        deck.Start();
        deck.Dispose();

        Assert.Equal(1, bass.FreeCalls);
        Assert.Equal(7, bass.LastFreedHandle);
        Assert.False(deck.IsRunning);
    }

    [Fact]
    public void Start_AfterDispose_Throws()
    {
        var deck = NewDeck(new FakeBassPlayback());
        deck.Dispose();

        Assert.Throws<ObjectDisposedException>(() => deck.Start());
    }

    [Fact]
    public void Start_WhenStreamCreationFails_LogsAndRethrows_StaysStopped()
    {
        var bass = new FakeBassPlayback
        {
            CreateStreamOverride = _ => throw new BassPlaybackException("boom")
        };
        var deck = NewDeck(bass);

        Assert.Throws<BassPlaybackException>(() => deck.Start());
        Assert.False(deck.IsRunning);
        Assert.Equal(0, bass.PlayCalls);
    }
}
