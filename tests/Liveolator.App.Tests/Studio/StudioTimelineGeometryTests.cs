using System.Collections.Generic;
using System.Reactive.Concurrency;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Studio;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Studio;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Studio;

/// <summary>
/// Guards the STUDIO timeline geometry invariants that keep the playhead and dropped clips on true
/// time-0 and the scroll extent tracking the material: (a) the gutter the playhead math uses is the
/// same number the lane-header column is sized to (one source of truth), and (b) the scrollable content
/// width is the arrangement duration (+ trailing margin) scaled by the zoom.
/// </summary>
public sealed class StudioTimelineGeometryTests
{
    private const double Tol = 1e-9;

    public StudioTimelineGeometryTests()
    {
        // Run the VM's reactive command / UI marshalling synchronously in tests.
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    private static StudioViewModel BuildViewModel()
    {
        var library = new MusicLibrary(new FakeFileEnumerator(), new FakeAudioDecoder());
        return new StudioViewModel(library, new FakeStudioProjectStore());
    }

    // --- (a) one gutter source of truth ---

    [Fact]
    public void LaneGutter_BindableWidth_EqualsTheConstantUsedByPlayheadMath()
    {
        // The header ColumnDefinition binds to LaneGutterWidth; the playhead/drop math uses LaneGutterPx.
        // They MUST be the same number or the playhead and clips drift off time-0.
        Assert.Equal(StudioViewModel.LaneGutterPx, StudioViewModel.LaneGutterWidth, Tol);
    }

    [Fact]
    public void PlayheadX_AtTimeZero_SitsExactlyAtTheGutter()
    {
        StudioViewModel vm = BuildViewModel();
        vm.SeekTo(0);
        Assert.Equal(StudioViewModel.LaneGutterWidth, vm.PlayheadX, Tol);
    }

    [Fact]
    public void PlayheadX_IsGutterPlusSecondsTimesZoom()
    {
        StudioViewModel vm = BuildViewModel();
        vm.PixelsPerSecond = 10;
        vm.SeekTo(4);
        Assert.Equal(StudioViewModel.LaneGutterPx + 4 * 10, vm.PlayheadX, Tol);
    }

    // --- (b) content width tracks duration * zoom ---

    [Fact]
    public void TimelineContentWidth_TracksDurationTimesZoom_PlusMargin()
    {
        StudioViewModel vm = BuildViewModel();
        vm.PixelsPerSecond = 10;
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 0); // null track -> 60s default length

        // duration (60) + trailing margin (8) all at 10 px/s = 680px.
        Assert.Equal((vm.ProjectDurationSeconds + 8) * 10, vm.TimelineContentWidth, Tol);
    }

    [Fact]
    public void TimelineContentWidth_GrowsWithZoom()
    {
        StudioViewModel vm = BuildViewModel();
        // A clip well past the empty-project floor (long span) so zoom, not the minimum, governs.
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 200);

        vm.PixelsPerSecond = 8;
        double atEight = vm.TimelineContentWidth;
        vm.PixelsPerSecond = 16;
        double atSixteen = vm.TimelineContentWidth;

        Assert.True(atEight > 600, "sanity: this case should be above the floor");
        Assert.Equal(atEight * 2, atSixteen, Tol);
    }

    [Fact]
    public void TimelineContentWidth_HasAMinimumForNearEmptyProjects()
    {
        StudioViewModel vm = BuildViewModel();
        vm.PixelsPerSecond = 2; // tiny zoom, no clips -> would be far below the floor

        Assert.True(vm.TimelineContentWidth >= 600, "content width must never collapse below the floor");
    }

    private sealed class FakeStudioProjectStore : IStudioProjectStore
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new List<string>());

        public Task<StudioProject?> LoadAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult<StudioProject?>(null);

        public Task SaveAsync(StudioProject project, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
