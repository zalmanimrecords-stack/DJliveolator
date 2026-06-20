using System.Reactive.Concurrency;
using System.Linq;
using Liveolator.App.Features.Studio;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// Tests the VIEW → Zoom commands: zoom in/out step the timeline scale by a fixed ratio and clamp to the
/// slider's range, and reset returns to the default scale. These back the VIEW menu's discrete zoom entries.
/// </summary>
public sealed class StudioZoomTests
{
    public StudioZoomTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static StudioViewModel BuildViewModel()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        return new StudioViewModel(library, new FakeStudioProjectStore());
    }

    [Fact]
    public void ZoomIn_IncreasesScale()
    {
        StudioViewModel vm = BuildViewModel();
        double before = vm.PixelsPerSecond;

        vm.ZoomInCommand.Execute().Subscribe();

        Assert.True(vm.PixelsPerSecond > before);
    }

    [Fact]
    public void ZoomOut_DecreasesScale()
    {
        StudioViewModel vm = BuildViewModel();
        double before = vm.PixelsPerSecond;

        vm.ZoomOutCommand.Execute().Subscribe();

        Assert.True(vm.PixelsPerSecond < before);
    }

    [Fact]
    public void ZoomIn_ClampsAtTheSliderMaximum()
    {
        StudioViewModel vm = BuildViewModel();
        for (int i = 0; i < 50; i++)
            vm.ZoomInCommand.Execute().Subscribe();

        Assert.Equal(200.0, vm.PixelsPerSecond, 1e-9); // matches the zoom slider's Maximum (deep magnification)
    }

    [Fact]
    public void ZoomOut_ClampsAtTheSliderMinimum()
    {
        StudioViewModel vm = BuildViewModel();
        for (int i = 0; i < 50; i++)
            vm.ZoomOutCommand.Execute().Subscribe();

        Assert.Equal(2.0, vm.PixelsPerSecond, 1e-9); // matches the zoom slider's Minimum
    }

    [Fact]
    public void ResetZoom_ReturnsToTheDefaultScale()
    {
        StudioViewModel vm = BuildViewModel();
        vm.ZoomInCommand.Execute().Subscribe();
        vm.ZoomInCommand.Execute().Subscribe();

        vm.ResetZoomCommand.Execute().Subscribe();

        Assert.Equal(8.0, vm.PixelsPerSecond, 1e-9); // DefaultPixelsPerSecond
    }

    private sealed class FakeStudioProjectStore : IStudioProjectStore
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(System.Array.Empty<string>());

        public Task<StudioProject?> LoadAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<StudioProject?>(null);

        public Task SaveAsync(StudioProject project, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
