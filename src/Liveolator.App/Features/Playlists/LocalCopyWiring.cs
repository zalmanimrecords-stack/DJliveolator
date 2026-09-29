using Liveolator.Core.Library.Music;
using Liveolator.Core.Persistence;
using Liveolator.Core.Playlist.LocalCopy;

namespace Liveolator.App.Features.Playlists;

/// <summary>
/// What the playlists window needs to copy a set to this computer. The library folders and the adoption
/// step belong to the LIBRARIES view model, which owns the folder list and the visible rows, so they come
/// in as delegates rather than as that view model itself.
/// </summary>
/// <param name="Service">Copies the files and writes their rows, cues and the "(local)" playlist.</param>
/// <param name="LibraryFolders">The current library folders, read on the UI thread.</param>
/// <param name="AdoptCopies">Registers the destination as a library folder and shows the new rows; called on the UI thread.</param>
/// <param name="Settings">Remembers the last destination.</param>
public sealed record LocalCopyWiring(
    PlaylistLocalCopyService Service,
    Func<IReadOnlyList<string>> LibraryFolders,
    Action<string, IReadOnlyList<MusicTrack>> AdoptCopies,
    ISettingsStore Settings);
