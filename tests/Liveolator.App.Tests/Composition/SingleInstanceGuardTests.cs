using System;
using Liveolator.App.Composition;
using Xunit;

namespace Liveolator.App.Tests.Composition;

public sealed class SingleInstanceGuardTests
{
    // A unique name per test run so parallel CI processes never collide on the shared OS mutex.
    private static string UniqueName() => "Liveolator.Test.SingleInstance." + Guid.NewGuid().ToString("N");

    [Fact]
    public void FirstInstance_IsPrimary_SecondIsNot()
    {
        string name = UniqueName();

        using var first = new SingleInstanceGuard(name);
        using var second = new SingleInstanceGuard(name);

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
    }

    [Fact]
    public void AfterPrimaryReleases_NextInstanceBecomesPrimary()
    {
        string name = UniqueName();

        using (var first = new SingleInstanceGuard(name))
            Assert.True(first.IsPrimary);

        using var next = new SingleInstanceGuard(name);
        Assert.True(next.IsPrimary);
    }
}
