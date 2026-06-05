using System;
using Liveolator.App.Composition;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
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
    public void Build_RegistersTheCatalogStore_SoStatePersists()
    {
        using var provider = (ServiceProvider)ServiceConfig.Build();

        // Persistence is always wired (it needs no native deps), so the scanned catalog + scan folders
        // survive a restart whether or not Live Mode comes up.
        Assert.NotNull(provider.GetService<IMusicCatalogStore>());
    }

    [Fact]
    public void Build_AlwaysProvidesTheLiveTab_EvenWithoutNativeAudio()
    {
        using var provider = (ServiceProvider)ServiceConfig.Build();

        // The Live tab runs on a pure-managed ManualBeatClock, so it must resolve and be wired for
        // intent (the shared dispatcher) whether or not realtime BASS audio is present.
        var live = provider.GetService<LiveViewModel>();
        Assert.NotNull(live);
        Assert.True(live!.IsLiveModeEnabled);
        live.Dispose();
    }

    [Fact]
    public void Build_AlwaysRegistersTheOneDispatcher_RoutingMixerAndVisualHeadless()
    {
        // The single dispatcher (doc 04) is always present — the beat, mixer and visual handlers are
        // pure-managed, so the Live UI can drive them with no native audio. Only the realtime deck
        // engine + its audio beat clock are gated on native BASS.
        using var provider = (ServiceProvider)ServiceConfig.Build();

        var dispatcher = provider.GetService<IPerformanceActionDispatcher>();
        Assert.NotNull(dispatcher);

        // A mixer action is routed and reflected in feedback (handler is in the dispatcher).
        dispatcher!.Dispatch(new PerformanceAction(
            PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute, Value: 0.75));
        Assert.Equal(0.75, dispatcher.GetFeedback(PerformanceActionKind.MixerCrossfade).Value, precision: 3);

        // A visual toggle is routed and latched in feedback.
        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.VisualBlackout));
        Assert.True(dispatcher.GetFeedback(PerformanceActionKind.VisualBlackout).IsActive);
    }

    [Fact]
    public void Build_GatesRealtimeAudioOnNativeBass_orFallsBackCleanly()
    {
        using var provider = (ServiceProvider)ServiceConfig.Build();

        var beatClock = provider.GetService<IBeatClock>();
        var engine = provider.GetService<IAudioPlaybackEngine>();

        // The realtime audio services are all-or-nothing: the engine and its audio beat clock are
        // registered together, or (native BASS missing) neither is. The catalog browser works either way.
        if (engine is not null)
            Assert.NotNull(beatClock);
        else
            Assert.Null(beatClock);

        Assert.NotNull(provider.GetService<LibrariesViewModel>());
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
