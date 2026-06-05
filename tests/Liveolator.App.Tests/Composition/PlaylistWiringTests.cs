using Liveolator.App.Composition;
using Liveolator.App.Features.Dj;
using Liveolator.Core.Actions;
using Liveolator.Core.Playlist;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Liveolator.App.Tests.Composition;

/// <summary>
/// Asserts the composition root wires the live playlist (the DJ "set") and the DJ tab, and that the
/// playlist actions route through the one dispatcher (doc 04/09).
/// </summary>
public sealed class PlaylistWiringTests
{
    [Fact]
    public void Build_RegistersLivePlaylist_AndDjTab()
    {
        using var provider = (ServiceProvider)ServiceConfig.Build();

        Assert.NotNull(provider.GetService<ILivePlaylist>());
        Assert.NotNull(provider.GetService<DjViewModel>());
    }

    [Fact]
    public void Dispatcher_RoutesPlaylistEdits_ToTheRegisteredQueue()
    {
        using var provider = (ServiceProvider)ServiceConfig.Build();
        var dispatcher = provider.GetRequiredService<IPerformanceActionDispatcher>();
        var playlist = provider.GetRequiredService<ILivePlaylist>();

        playlist.Load(new[] { "a.mp3", "b.mp3", "c.mp3" });
        Assert.Equal("a.mp3", playlist.Now!.TrackPath);

        // A skip action routed through the dispatcher advances the queue (interim immediate scheduler).
        dispatcher.Dispatch(new PerformanceAction(PerformanceActionKind.PlaylistSkipOnNextBar));

        Assert.Equal("b.mp3", playlist.Now!.TrackPath);
    }
}
