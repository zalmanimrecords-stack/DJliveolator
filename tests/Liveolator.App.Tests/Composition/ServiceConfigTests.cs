using System;
using Liveolator.App.Composition;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Visuals;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Liveolator.App.Tests.Composition;

/// <summary>
/// Asserts the composition root wires Live Mode and falls back cleanly when the native BASS
/// library is absent — the CI condition (no per-platform binaries are fetched there).
/// These tests exercise the wiring path without requiring native BASS: <see cref="ServiceConfig"/>
/// must build a working catalog-browser provider whether or not Live Mode comes up.
/// </summary>
public sealed class ServiceConfigTests
{
    [Fact]
    public void Build_AlwaysProvidesTheCatalogBrowser()
    {
        using var provider = (ServiceProvider)ServiceConfig.Build();

        // The catalog/library is the always-on baseline, independent of native audio.
        Assert.NotNull(provider.GetService<MusicLibrary>());
        Assert.NotNull(provider.GetService<LibrariesViewModel>());
    }

    [Fact]
    public void Build_AlwaysProvidesTheLiveTab_EvenWithoutNativeAudio()
    {
        using var provider = (ServiceProvider)ServiceConfig.Build();

        // The Live tab runs on a pure-managed ManualBeatClock, so it must resolve and be wired for
        // intent (its own dispatcher) whether or not realtime BASS audio is present.
        var live = provider.GetService<LiveViewModel>();
        Assert.NotNull(live);
        Assert.True(live!.IsLiveModeEnabled);
        live.Dispose();
    }

    [Fact]
    public void Build_WiresLiveModeConsistently_orFallsBackCleanly()
    {
        using var provider = (ServiceProvider)ServiceConfig.Build();

        var dispatcher = provider.GetService<IPerformanceActionDispatcher>();
        var beatClock = provider.GetService<IBeatClock>();
        var engine = provider.GetService<IAudioPlaybackEngine>();

        // Live Mode is all-or-nothing: WireLiveAudio registers the dispatcher, beat clock and
        // engine together, or (native BASS missing) registers none of them. There is no
        // half-wired state in which only some Live Mode services resolve.
        bool liveModeUp = dispatcher is not null;
        if (liveModeUp)
        {
            Assert.NotNull(beatClock);
            Assert.NotNull(engine);
        }
        else
        {
            // Clean fallback: catalog browser still works; transport services are simply absent.
            Assert.Null(beatClock);
            Assert.Null(engine);
            Assert.NotNull(provider.GetService<LibrariesViewModel>());
        }
    }

    [Fact]
    public void Build_RegistersVisualEngine_Headless_WithoutRunningTheRenderWindow()
    {
        // The GL engine must be resolvable for later on-demand rendering, but composing the provider
        // must NOT open a window/GL context (Run() is never called here) — the app launches headless.
        using var provider = (ServiceProvider)ServiceConfig.Build();

        Assert.NotNull(provider.GetService<IVisualPerformanceEngine>());
    }
}
