using System;
using System.Linq;
using System.Reactive.Concurrency;
using Liveolator.App.Shell;
using Liveolator.Core;
using Liveolator.Core.Library.Music;
using Liveolator.Core.Playlist;
using ReactiveUI;

namespace Liveolator.App.Features.Dj;

/// <summary>
/// The "PLAYING NEXT" readout on a deck's waveform: the head of that deck's live queue, which is where a
/// send onto a playing deck lands (<see cref="DeckTrackLoader"/>). Hidden when nothing is waiting.
/// </summary>
public sealed class DeckUpNextViewModel : ViewModelBase, IDisposable
{
    private readonly ILivePlaylist _queue;
    private readonly MusicLibrary _library;
    private string _nextTitle = string.Empty;

    public DeckUpNextViewModel(ILivePlaylist queue, MusicLibrary library)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _queue.Changed += OnQueueChanged;
        Update();
    }

    public string NextTitle
    {
        get => _nextTitle;
        private set
        {
            this.RaiseAndSetIfChanged(ref _nextTitle, value);
            this.RaisePropertyChanged(nameof(HasNext));
        }
    }

    public bool HasNext => NextTitle.Length > 0;

    public void Dispose() => _queue.Changed -= OnQueueChanged;

    // A bar-quantized skip edits the queue from the beat scheduler, off the UI thread.
    private void OnQueueChanged(object? sender, EventArgs e) => RxApp.MainThreadScheduler.Schedule(Update);

    private void Update()
    {
        QueueEntry? next = _queue.Upcoming.FirstOrDefault();
        NextTitle = next is null
            ? string.Empty
            : _library.TryGetByPathOrName(next.TrackPath)?.Title
              ?? PortablePath.GetFileNameWithoutExtension(next.TrackPath);
    }
}
