using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Libraries;
using Liveolator.App.Tests.Fakes;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Waveform;
using ReactiveUI;

namespace Liveolator.App.Tests.Libraries;

public sealed class LibraryWaveformCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changing_or_clearing_selection_cancels_obsolete_waveform_decode(bool clear)
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        var waveform = new SlowWaveformProvider();
        var library = new MusicLibrary(new FakeFileEnumerator(new[] { "/music/Alpha.wav", "/music/Beta.wav" }), new FakeAudioDecoder());
        using var vm = new LibrariesViewModel(library, waveformProvider: waveform, isLocallyDecodable: _ => true);
        vm.AddFolder("/music");
        await vm.ScanCommand.Execute().ToTask();
        vm.SelectedTrack = vm.Tracks.Single(track => track.Title == "Alpha");
        await waveform.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.SelectedTrack = clear ? null : vm.Tracks.Single(track => track.Title == "Beta");
        await waveform.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class SlowWaveformProvider : IWaveformProvider
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WaveformOverview> GetOverviewAsync(string filePath, int bucketCount, CancellationToken cancellationToken = default)
        {
            if (!filePath.EndsWith("Alpha.wav")) return WaveformOverview.Empty;
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            return WaveformOverview.Empty;
        }
    }
}
