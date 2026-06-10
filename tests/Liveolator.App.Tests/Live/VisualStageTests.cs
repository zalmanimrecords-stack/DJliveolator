using System;
using System.Threading;
using Liveolator.App.Features.Live;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.App.Tests.Live;

public sealed class VisualStageTests
{
    [Fact]
    public void Show_RunsTheWindowLoopVisible_OnABackgroundThread()
    {
        using var started = new ManualResetEventSlim(false);
        int runThreadId = 0;
        bool startedVisible = false;
        var stage = new VisualStage(
            visible => { startedVisible = visible; runThreadId = Environment.CurrentManagedThreadId; started.Set(); },
            present: () => { },
            NullLogger.Instance);

        stage.Show();

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, runThreadId);
        Assert.True(startedVisible);
    }

    [Fact]
    public void Start_RunsTheWindowLoopHidden()
    {
        using var started = new ManualResetEventSlim(false);
        bool startedVisible = true;
        var stage = new VisualStage(
            visible => { startedVisible = visible; started.Set(); },
            present: () => { },
            NullLogger.Instance);

        stage.Start();

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(startedVisible); // hidden render loop for the in-app preview
    }

    [Fact]
    public void Show_WhileHiddenLoopRuns_RevealsViaPresent_WithoutRestarting()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int runCalls = 0;
        int presentCalls = 0;
        var stage = new VisualStage(
            visible => { Interlocked.Increment(ref runCalls); entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); },
            present: () => Interlocked.Increment(ref presentCalls),
            NullLogger.Instance);

        stage.Start();                                    // hidden loop running
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        stage.Show();                                     // reveal: must NOT start a second loop
        Assert.True(stage.IsShown);
        release.Set();

        SpinWait.SpinUntil(() => !stage.IsShown, TimeSpan.FromSeconds(5));
        Assert.Equal(1, runCalls);                        // only one render loop ever launched
        Assert.Equal(1, presentCalls);                    // reveal went through the present delegate
    }

    [Fact]
    public void Show_IsIdempotent_WhileTheWindowIsRunning()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int calls = 0;
        var stage = new VisualStage(
            visible => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); },
            present: () => { },
            NullLogger.Instance);

        stage.Show();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        stage.Show();                 // second call must not launch another loop while running
        Assert.True(stage.IsShown);
        release.Set();

        SpinWait.SpinUntil(() => !stage.IsShown, TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Show_SwallowsRunWindowFailure_AndNeverThrows()
    {
        using var ran = new ManualResetEventSlim(false);
        var stage = new VisualStage(
            visible => { ran.Set(); throw new InvalidOperationException("no display"); },
            present: () => { },
            NullLogger.Instance);

        Exception? ex = Record.Exception(() => stage.Show());

        Assert.Null(ex); // launching never throws into the caller
        Assert.True(ran.Wait(TimeSpan.FromSeconds(5)));
        SpinWait.SpinUntil(() => !stage.IsShown, TimeSpan.FromSeconds(5));
        Assert.False(stage.IsShown);
    }
}
