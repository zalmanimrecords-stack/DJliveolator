using System;
using Liveolator.App.Composition;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Features.Live;
using Liveolator.App.Shell;
using Liveolator.Audio.Playback;
using Liveolator.Core.Actions;
using Liveolator.Core.Audio;
using Liveolator.Core.Beat;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
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
        using var provider = BuildForTest();

        // The catalog/library is the always-on baseline, independent of native audio.
        Assert.NotNull(provider.GetService<MusicLibrary>());
        Assert.NotNull(provider.GetService<LibrariesViewModel>());
    }

    [Fact]
    public void Build_ResolvesTheMainWindow_WithShellStatus()
    {
        // The main window is the app's root DataContext; it depends on ShellStatusViewModel, which in
        // turn needs IMidiControlStatus (the MidiControlSession) + AppSettings. Resolving it through the
        // real container guards against a composition gap that would crash the app on launch but slip
        // past tests that never resolve the root (the app-shell + integration merge had exactly that gap).
        using var provider = BuildForTest();

        Assert.NotNull(provider.GetService<ShellStatusViewModel>());
        Assert.NotNull(provider.GetService<MainWindowViewModel>());
    }

    [Fact]
    public void Build_RegistersTheCatalogStore_SoStatePersists()
    {
        using var provider = BuildForTest();

        // Persistence is always wired (it needs no native deps), so the scanned catalog + scan folders
        // survive a restart whether or not Live Mode comes up.
        Assert.NotNull(provider.GetService<IMusicCatalogStore>());
    }

    [Fact]
    public void Build_AlwaysProvidesTheLiveTab_EvenWithoutNativeAudio()
    {
        using var provider = BuildForTest();

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
        using var provider = BuildForTest();

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
        using var provider = BuildForTest();

        var beatClock = provider.GetService<IBeatClock>();
        var engine = provider.GetService<IMultiDeckPlaybackEngine>();

        // The realtime audio services are all-or-nothing: the two-deck engine and the master-mix beat
        // clock are registered together, or (native BASS/bassmix missing) neither is. The catalog browser
        // works either way.
        if (engine is not null)
            Assert.NotNull(beatClock);
        else
            Assert.Null(beatClock);

        Assert.NotNull(provider.GetService<LibrariesViewModel>());
    }

    [Fact]
    public void Build_RegistersTheLiveProfileStore_SoAuthoredDataPersists()
    {
        using var provider = BuildForTest();

        // The Live-Mode profile store (mapping profiles / scenes / macros / autopilot rule-sets, doc 13)
        // needs no native deps, so it is always wired for the host to load/save snapshots.
        Assert.NotNull(provider.GetService<ILiveProfileStore>());
        Assert.NotNull(provider.GetService<ITrackVisualProgramStore>());
    }

    [Fact]
    public void Build_BindsTheLiveQueueAudio_OnlyWhenTheRealtimeEngineIsUp()
    {
        using var provider = BuildForTest();

        // The playlist→audio binding follows the realtime engine: present together, or (no native BASS)
        // neither — the queue still edits freely in catalog-browser mode.
        var engine = provider.GetService<IMultiDeckPlaybackEngine>();
        var player = provider.GetService<PlaylistAudioPlayer>();
        if (engine is not null)
            Assert.NotNull(player);
        else
            Assert.Null(player);
    }

    [Fact]
    public void Build_RegistersVisualEngine_Headless_WithoutRunningTheRenderWindow()
    {
        // The GL engine must be resolvable for later on-demand rendering, but composing the provider
        // must NOT open a window/GL context (Run() is never called here) — the app launches headless.
        using var provider = BuildForTest();

        Assert.NotNull(provider.GetService<IVisualPerformanceEngine>());
    }

    [Fact]
    public void Build_RegistersTheLiveAudioLevelSource_SilentWhenHeadless()
    {
        // No native BASS in CI ⇒ no master mix ⇒ the visual level source falls back to silence rather
        // than the visual engine taking an optional dependency (doc 26 headless rule).
        using var provider = BuildForTest();

        var level = provider.GetService<IVisualAudioLevelSource>();

        Assert.NotNull(level);
        Assert.Equal(VisualAudioLevel.Silent, level!.Current);
    }

    [Fact]
    public void Build_RegistersTheBuiltInVuMeterGenerator()
    {
        // The reference generator add-on (doc 26) is registered out of the box so a generator layer can
        // reference it and render a live meter.
        using var provider = BuildForTest();

        var registry = provider.GetRequiredService<IVisualEffectRegistry>();
        bool found = registry.TryGet(VuMeterAddon.EffectId, version: null, out VisualEffectDescriptor descriptor);

        Assert.True(found);
        Assert.Equal(VisualEffectRole.Generator, descriptor.Role);
    }

    [Fact]
    public void Build_RegistersTheBuiltInPsyFractalGenerator()
    {
        using var provider = BuildForTest();

        var registry = provider.GetRequiredService<IVisualEffectRegistry>();
        bool found = registry.TryGet(
            PsyFractalVisualizerAddon.EffectId,
            version: null,
            out VisualEffectDescriptor descriptor);

        Assert.True(found);
        Assert.Equal(VisualEffectRole.Generator, descriptor.Role);
        Assert.Contains(descriptor.Parameters, parameter => parameter.Id == "symmetry");
        Assert.Contains(descriptor.Parameters, parameter => parameter.Id == "palette");
    }

    private static ServiceProvider BuildForTest() =>
        (ServiceProvider)ServiceConfig.Build(enableSystemMetrics: false);
}
