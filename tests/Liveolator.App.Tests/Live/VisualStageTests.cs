using System;
using System.Threading;
using Liveolator.App.Features.Live;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.App.Tests.Live;

public sealed class VisualStageTests
{
    [Fact]
    public void Show_RunsTheWindowLoop_OnABackgroundThread()
    {
        using var started = new ManualResetEventSlim(false);
        int runThreadId = 0;
        var stage = new VisualStage(
            () => { runThreadId = Environment.CurrentManagedThreadId; started.Set(); },
            NullLogger.Instance);

        stage.Show();

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, runThreadId);
    }

    [Fact]
    public void Show_IsIdempotent_WhileTheWindowIsRunning()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        int calls = 0;
        var stage = new VisualStage(
            () => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); },
            NullLogger.Instance);

        stage.Show();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        stage.Show();                 // second call must be a no-op while running
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
            () => { ran.Set(); throw new InvalidOperationException("no display"); },
            NullLogger.Instance);

        Exception? ex = Record.Exception(() => stage.Show());

        Assert.Null(ex); // launching never throws into the caller
        Assert.True(ran.Wait(TimeSpan.FromSeconds(5)));
        SpinWait.SpinUntil(() => !stage.IsShown, TimeSpan.FromSeconds(5));
        Assert.False(stage.IsShown);
    }
}
