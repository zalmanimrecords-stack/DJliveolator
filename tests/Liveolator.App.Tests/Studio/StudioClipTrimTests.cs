using System;
using Liveolator.App.Features.Studio;
using Liveolator.Core.Studio;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// Guards the clip edge-trim math behind the timeline's drag handles: the right edge changes the clip
/// length (source-out), the left edge trims the head and shifts the start so the rest stays anchored,
/// both honour the warp factor (source seconds per timeline second), and both clamp so a clip can't be
/// collapsed past itself or its source bounds.
/// </summary>
public sealed class StudioClipTrimTests
{
    private const double Tol = 1e-9;

    private static StudioClipViewModel Clip(
        double start, double? sourceOut, double sourceIn = 0,
        bool warp = false, double sourceBpm = 0, double targetBpm = 120)
        => new(
            new StudioClip(0, "/m/a.wav", start, TimeSpan.FromSeconds(sourceIn),
                sourceOut is { } o ? TimeSpan.FromSeconds(o) : null, SourceBpm: sourceBpm, WarpEnabled: warp),
            track: null,
            pixelsPerSecond: 8) { WarpTargetBpm = targetBpm };

    [Fact]
    public void DragEndEdge_ExtendsTheClipLength()
    {
        StudioClipViewModel clip = Clip(start: 0, sourceOut: 10);

        clip.DragEndEdge(2); // pull the tail out by 2 timeline-seconds

        Assert.Equal(12, clip.SourceOutSeconds!.Value, Tol);
    }

    [Fact]
    public void DragEndEdge_CannotCollapsePastTheHead()
    {
        StudioClipViewModel clip = Clip(start: 0, sourceOut: 10);

        clip.DragEndEdge(-100);

        Assert.True(clip.SourceOutSeconds!.Value > 0); // clamped to head + a minimum, never inverted
        Assert.True(clip.SourceOutSeconds!.Value < 0.1);
    }

    [Fact]
    public void DragStartEdge_TrimsTheHeadAndShiftsTheStart_RestStaysAnchored()
    {
        StudioClipViewModel clip = Clip(start: 5, sourceOut: 10, sourceIn: 0);

        clip.DragStartEdge(2); // push the head in by 2 timeline-seconds

        Assert.Equal(2, clip.SourceInSeconds, Tol);
        Assert.Equal(7, clip.TimelineStartSeconds, Tol); // start moved by the same time so audio stays put
    }

    [Fact]
    public void DragStartEdge_ExpandingPastTheFileStart_ClampsToZeroAndStopsMovingTheStart()
    {
        StudioClipViewModel clip = Clip(start: 5, sourceOut: 10, sourceIn: 3);

        clip.DragStartEdge(-100); // try to expand the head far before the file start

        Assert.Equal(0, clip.SourceInSeconds, Tol);  // can't read before the file start
        Assert.Equal(2, clip.TimelineStartSeconds, Tol); // start moved left only by the 3s actually recovered
    }

    [Fact]
    public void DragEndEdge_HonoursTheWarpFactor()
    {
        // Warp 120→60 BPM ⇒ factor 0.5 source-seconds per timeline-second. A 2s timeline drag adds 1s of source.
        StudioClipViewModel clip = Clip(start: 0, sourceOut: 10, warp: true, sourceBpm: 120, targetBpm: 60);

        clip.DragEndEdge(2);

        Assert.Equal(11, clip.SourceOutSeconds!.Value, Tol);
    }

    [Fact]
    public void DragFadeIn_LengthensTheHeadFade()
    {
        StudioClipViewModel clip = Clip(start: 0, sourceOut: 10);

        clip.DragFadeIn(2);

        Assert.Equal(2, clip.FadeInSeconds, Tol);
    }

    [Fact]
    public void DragFadeOut_LengthensTheTailFade()
    {
        StudioClipViewModel clip = Clip(start: 0, sourceOut: 10);

        clip.DragFadeOut(1.5);

        Assert.Equal(1.5, clip.FadeOutSeconds, Tol);
    }

    [Fact]
    public void DragFade_ClampsToTheClipLengthAndZero()
    {
        StudioClipViewModel clip = Clip(start: 0, sourceOut: 10); // 10s timeline length (unwarped)

        clip.DragFadeIn(1000);                       // can't exceed the clip
        Assert.Equal(10, clip.FadeInSeconds, Tol);

        clip.DragFadeOut(-100);                       // can't go negative
        Assert.Equal(0, clip.FadeOutSeconds, Tol);
    }
}
