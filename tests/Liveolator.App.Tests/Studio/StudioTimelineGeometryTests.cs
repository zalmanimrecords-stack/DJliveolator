using System.Collections.Generic;
using System.Linq;
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
/// Guards the STUDIO timeline geometry invariants. The lane headers (deck label + automation picker)
/// live in a fixed left column OUTSIDE the horizontal scroll, so the timeline content has its own
/// origin at time-0: the playhead and clips share that content coordinate space (X = seconds * zoom,
/// no gutter offset), and the scrollable content width is the arrangement duration (+ trailing margin)
/// scaled by the zoom. (a) the bindable gutter width is the single source of truth for the header
/// column, and (b) content width tracks duration * zoom.
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

    // --- (a) the timeline content shares a time-0 origin (headers are a separate fixed column) ---

    [Fact]
    public void PlayheadX_AtTimeZero_SitsAtTheContentOrigin()
    {
        // The playhead lives inside the content scroller (the headers are a separate fixed column), so
        // time-0 is the content's left edge, x = 0.
        StudioViewModel vm = BuildViewModel();
        vm.SeekTo(0);
        Assert.Equal(0.0, vm.PlayheadX, Tol);
    }

    [Fact]
    public void PlayheadX_IsSecondsTimesZoom()
    {
        StudioViewModel vm = BuildViewModel();
        vm.PixelsPerSecond = 10;
        vm.SeekTo(4);
        Assert.Equal(4 * 10, vm.PlayheadX, Tol);
    }

    [Fact]
    public void PlayheadX_MatchesAClipXAtTheSameTime()
    {
        // Playhead and clips share the content coordinate space, so the playhead at time T lands exactly
        // on the left edge of a clip that starts at T — they can never drift apart.
        StudioViewModel vm = BuildViewModel();
        vm.PixelsPerSecond = 12;
        vm.AddClipAt("/m/a.wav", deckSlot: 0, startSeconds: 5);
        StudioClipViewModel clip = vm.Lanes.SelectMany(l => l.Clips).Single();

        vm.SeekTo(clip.TimelineStartSeconds);

        Assert.Equal(clip.X, vm.PlayheadX, Tol);
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
