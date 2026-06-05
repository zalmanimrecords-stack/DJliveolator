using System;
using System.Collections.Generic;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class DeckActionHandlerTests
{
    private sealed class FakePlaybackEngine : IAudioPlaybackEngine
    {
        public List<string> Loaded { get; } = new();
        public int PlayPauseCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool IsPlaying { get; set; }

        public void Load(string trackPath) => Loaded.Add(trackPath);
        public void PlayPause() => PlayPauseCalls++;
        public void Stop() => StopCalls++;
    }

    [Fact]
    public void HandledKinds_AreLoadPlayPauseAndStop()
    {
        var handler = new DeckActionHandler(new FakePlaybackEngine());

        Assert.Contains(PerformanceActionKind.DeckLoadTrack, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.DeckPlayPause, handler.HandledKinds);
        Assert.Contains(PerformanceActionKind.TransportStop, handler.HandledKinds);
    }

    [Fact]
    public void LoadTrack_PassesArgumentPathToEngine()
    {
        var engine = new FakePlaybackEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckLoadTrack, Argument: @"C:\song.flac"));

        Assert.Equal(@"C:\song.flac", Assert.Single(engine.Loaded));
    }

    [Fact]
    public void LoadTrack_WithoutArgument_Throws()
    {
        var handler = new DeckActionHandler(new FakePlaybackEngine());

        Assert.Throws<ArgumentException>(
            () => handler.Handle(new PerformanceAction(PerformanceActionKind.DeckLoadTrack)));
    }

    [Fact]
    public void PlayPauseAndStop_RouteToEngine()
    {
        var engine = new FakePlaybackEngine();
        var handler = new DeckActionHandler(engine);

        handler.Handle(new PerformanceAction(PerformanceActionKind.DeckPlayPause));
        handler.Handle(new PerformanceAction(PerformanceActionKind.TransportStop));

        Assert.Equal(1, engine.PlayPauseCalls);
        Assert.Equal(1, engine.StopCalls);
    }

    [Fact]
    public void Feedback_ReflectsPlayState()
    {
        var engine = new FakePlaybackEngine { IsPlaying = true };
        var handler = new DeckActionHandler(engine);

        ActionFeedbackState fb = handler.GetFeedback(PerformanceActionKind.DeckPlayPause, slot: 0);

        Assert.True(fb.IsActive);
        Assert.True(fb.IsAvailable);
    }

    [Fact]
    public void RoutesThroughDispatcher_EndToEnd()
    {
        var engine = new FakePlaybackEngine();
        var dispatcher = new PerformanceActionDispatcher(
            new[] { new DeckActionHandler(engine) },
            NullLogger<PerformanceActionDispatcher>.Instance);

        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckLoadTrack, Argument: @"C:\x.wav"));
        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.DeckPlayPause));

        Assert.Single(engine.Loaded);
        Assert.Equal(1, engine.PlayPauseCalls);
    }
}
