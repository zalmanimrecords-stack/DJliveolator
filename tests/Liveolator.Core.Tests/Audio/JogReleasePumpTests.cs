using System;
using System.Threading;
using Liveolator.Core.Audio;
using Xunit;

namespace Liveolator.Core.Tests.Audio;

public class JogReleasePumpTests
{
    [Fact]
    public void Start_InvokesTheCallbackRepeatedly_UntilDisposed()
    {
        var fired = new CountdownEvent(3);
        using var pump = new JogReleasePump(
            () => { if (!fired.IsSet) fired.Signal(); },
            TimeSpan.FromMilliseconds(2));

        Assert.False(pump.IsRunning);
        pump.Start();
        Assert.True(pump.IsRunning);
        Assert.True(fired.Wait(TimeSpan.FromSeconds(2)), "pump should tick the callback repeatedly");

        pump.Dispose();
        Assert.False(pump.IsRunning);
    }

    [Fact]
    public void AThrowingCallback_DoesNotKillThePump()
    {
        int calls = 0;
        var reached = new CountdownEvent(3);
        using var pump = new JogReleasePump(
            () =>
            {
                Interlocked.Increment(ref calls);
                if (!reached.IsSet) reached.Signal();
                throw new InvalidOperationException("boom");
            },
            TimeSpan.FromMilliseconds(2));

        pump.Start();

        // Despite every tick throwing, the loop keeps ticking (failures are logged, not fatal).
        Assert.True(reached.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void NullCallback_Throws()
        => Assert.Throws<ArgumentNullException>(() => new JogReleasePump(null!));

    [Fact]
    public void NonPositiveInterval_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new JogReleasePump(() => { }, TimeSpan.Zero));
}
