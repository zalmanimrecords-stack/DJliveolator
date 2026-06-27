using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Liveolator.App.Features.Shared;
using Liveolator.App.Shell;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
using Liveolator.Core.Visuals;
using ReactiveUI;

namespace Liveolator.App.Features.VisualLibrary;

/// <summary>
/// The VJ / Visual Library tab (Track C C1). Mirrors the music Libraries tab over the existing visual
/// catalog (<see cref="VisualMediaLibrary"/>): adds folders, runs the incremental scan (probing
/// image/video dimensions + video duration), and exposes the catalogued assets with a text search and
/// a kind + status filter. All filter/scan/restore logic lives here and is unit-tested; scanning and
/// persistence reuse the same Core library + catalog-store seam the MCP <c>scan_visual_folders</c> tool
/// uses, so there is one tested scan/query path. Holds no Avalonia types.
/// </summary>
public sealed class VisualLibraryViewModel : ViewModelBase
{
    // Longest edge of the generated preview thumbnail. The detail panel is ~320px wide; this keeps the
    // decode + bitmap cheap (and bounds memory) while staying crisp on hi-dpi displays.
    private const int PreviewMaxEdge = 480;

    // Longest edge of a grid-view tile thumbnail (tiles are ~150px; this stays crisp on hi-dpi).
    private const int ThumbnailMaxEdge = 240;

    private readonly VisualMediaLibrary _library;
    private readonly IVisualCatalogStore? _store;
    private readonly IVisualThumbnailRenderer? _thumbnails;
    private readonly IFileRemover? _fileRemover;
    private readonly IConfirmationService? _confirmation;
    private List<VisualAssetRowViewModel> _all = new();

    // Caps concurrent grid-thumbnail renders so scrolling a large (possibly network-drive) catalog never
    // fires hundreds of ffmpeg/decode jobs at once. ponytail: fixed cap; make it adaptive only if needed.
    private readonly SemaphoreSlim _thumbnailGate = new(3);
    // Paths already requested, so a row renders its thumbnail at most once even as it scrolls in and out.
    private readonly HashSet<string> _thumbnailRequested = new(StringComparer.OrdinalIgnoreCase);
    private bool _showThumbnails;

    private string? _searchText;
    private VisualMediaKind? _selectedKind;
    private MediaAnalysisStatus? _selectedStatus;
    private bool _suppressFilter;
    private VisualAssetRowViewModel? _selectedAsset;
    private string _scanStatus = "Add folders, then Scan.";
    private bool _isScanning;
    private double _scanProgressValue;

    private WriteableBitmap? _previewBitmap;
    private bool _isPreviewLoading;
    private string? _previewMessage;
    // Cancels the in-flight preview render when the selection changes again before it finishes, so a
    // slow video-frame extraction never paints over a newer selection.
    private CancellationTokenSource? _previewCts;

    /// <param name="library">The Core visual-media library (scan/catalog).</param>
    /// <param name="store">Persists the catalog + scan folders across runs; null disables persistence
    /// (the tab still works in-memory for the session).</param>
    /// <param name="thumbnails">Renders the selected asset's preview (image decode / video frame); null
    /// disables the preview (the panel shows a placeholder).</param>
    /// <param name="fileRemover">Deletes an asset's file from disk; null disables the delete action.</param>
    /// <param name="confirmation">Confirms the destructive delete; null disables the delete action.</param>
    public VisualLibraryViewModel(
        VisualMediaLibrary library,
        IVisualCatalogStore? store = null,
        IVisualThumbnailRenderer? thumbnails = null,
        IFileRemover? fileRemover = null,
        IConfirmationService? confirmation = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store;
        _thumbnails = thumbnails;
        _fileRemover = fileRemover;
        _confirmation = confirmation;

        ScanCommand = ReactiveCommand.CreateFromTask(
            RunScanAsync,
            this.WhenAnyValue(x => x.IsScanning, scanning => !scanning));
        ClearFiltersCommand = ReactiveCommand.Create(ClearFilters);
        RemoveFolderCommand = ReactiveCommand.Create<string>(RemoveFolder);
        DeleteSelectedCommand = ReactiveCommand.CreateFromTask(
            () => DeleteAssetAsync(SelectedAsset),
            this.WhenAnyValue(x => x.SelectedAsset, asset => CanDelete(asset)));
        // Row-level delete (list/grid context menu): always available when delete is wired — it carries
        // its own target row, so it does not depend on the detail-panel selection.
        DeleteAssetCommand = ReactiveCommand.CreateFromTask<VisualAssetRowViewModel>(
            DeleteAssetAsync, Observable.Return(_fileRemover is not null && _confirmation is not null));

        Observable.Merge(
                this.WhenAnyValue(x => x.SearchText).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedKind).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedStatus).Select(_ => Unit.Default))
            .Subscribe(_ => ApplyFilter());

        // Load the preview whenever the selection changes (and clear it when nothing is selected).
        this.WhenAnyValue(x => x.SelectedAsset).Subscribe(row => _ = LoadPreviewAsync(row));
    }

    public ObservableCollection<string> Folders { get; } = new();
    public ObservableCollection<VisualAssetRowViewModel> Assets { get; } = new();

    /// <summary>The kind-filter choices (null = "Any"); fixed, so it is built once.</summary>
    public IReadOnlyList<VisualMediaKind?> KindOptions { get; } = new VisualMediaKind?[]
    {
        null, VisualMediaKind.Image, VisualMediaKind.Video,
    };

    /// <summary>The status-filter choices (null = "Any").</summary>
    public IReadOnlyList<MediaAnalysisStatus?> StatusOptions { get; } = new MediaAnalysisStatus?[]
    {
        null, MediaAnalysisStatus.Ok, MediaAnalysisStatus.PartiallyAnalyzed, MediaAnalysisStatus.Failed,
    };

    public ReactiveCommand<Unit, Unit> ScanCommand { get; }

    /// <summary>Resets the kind/status filter and the search box back to "show all".</summary>
    public ReactiveCommand<Unit, Unit> ClearFiltersCommand { get; }

    /// <summary>Removes a scan folder (parameter) from the set and re-persists. Does not delete files;
    /// the already-catalogued assets stay until the next scan re-derives the list.</summary>
    public ReactiveCommand<string, Unit> RemoveFolderCommand { get; }

    /// <summary>Permanently deletes the selected asset's file from disk (after confirmation) and drops
    /// it from the catalog. Enabled only when an asset is selected and delete is wired.</summary>
    public ReactiveCommand<Unit, Unit> DeleteSelectedCommand { get; }

    /// <summary>Permanently deletes a specific row's file (the list/grid context-menu delete). Carries
    /// its own target, so it works without first selecting the row into the detail panel.</summary>
    public ReactiveCommand<VisualAssetRowViewModel, Unit> DeleteAssetCommand { get; }

    /// <summary>When true the assets show as a thumbnail grid; otherwise the text table.</summary>
    public bool ShowThumbnails
    {
        get => _showThumbnails;
        set => this.RaiseAndSetIfChanged(ref _showThumbnails, value);
    }

    public string? SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    /// <summary>Selected kind filter (null = all kinds).</summary>
    public VisualMediaKind? SelectedKind
    {
        get => _selectedKind;
        set => this.RaiseAndSetIfChanged(ref _selectedKind, value);
    }

    /// <summary>Selected analysis-status filter (null = any status).</summary>
    public MediaAnalysisStatus? SelectedStatus
    {
        get => _selectedStatus;
        set => this.RaiseAndSetIfChanged(ref _selectedStatus, value);
    }

    public VisualAssetRowViewModel? SelectedAsset
    {
        get => _selectedAsset;
        set => this.RaiseAndSetIfChanged(ref _selectedAsset, value);
    }

    /// <summary>The decoded preview thumbnail of the selected asset, or null while loading / when no
    /// preview is available (then <see cref="PreviewMessage"/> explains why).</summary>
    public WriteableBitmap? PreviewBitmap
    {
        get => _previewBitmap;
        private set => this.RaiseAndSetIfChanged(ref _previewBitmap, value);
    }

    /// <summary>True while a preview is being rendered (drives a "Loading…" hint).</summary>
    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        private set => this.RaiseAndSetIfChanged(ref _isPreviewLoading, value);
    }

    /// <summary>A short explanation shown in place of the preview when one cannot be produced
    /// (e.g. ffmpeg missing for a video, or an undecodable image); null when a preview is shown.</summary>
    public string? PreviewMessage
    {
        get => _previewMessage;
        private set => this.RaiseAndSetIfChanged(ref _previewMessage, value);
    }

    public string ScanStatus
    {
        get => _scanStatus;
        private set => this.RaiseAndSetIfChanged(ref _scanStatus, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => this.RaiseAndSetIfChanged(ref _isScanning, value);
    }

    /// <summary>Overall scan progress (0–100) for the progress bar.</summary>
    public double ScanProgressValue
    {
        get => _scanProgressValue;
        private set => this.RaiseAndSetIfChanged(ref _scanProgressValue, value);
    }

    /// <summary>
    /// Restores the previously-persisted state (scan folders + catalogued assets) so the tab opens
    /// where the last run left off. Called once at startup. A persistence failure degrades to an empty
    /// session with a surfaced status — it never blocks the app (global standards #16/#26).
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_store is null)
            return;

        try
        {
            IReadOnlyList<string> folders = await _store.LoadVisualScanFoldersAsync(cancellationToken).ConfigureAwait(false);
            IReadOnlyList<VisualAsset> cached = await _store.LoadVisualAsync(cancellationToken).ConfigureAwait(false);

            List<VisualAssetRowViewModel>? rows = null;
            if (cached.Count > 0)
            {
                _library.Restore(cached);
                rows = BuildRows();
            }

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                foreach (string folder in folders)
                    if (!Folders.Contains(folder))
                        Folders.Add(folder);

                if (rows is not null)
                {
                    _all = rows;
                    ApplyFilter();
                    ScanStatus = $"{rows.Count} assets (restored)";
                }
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not restore saved visual library: {ex.Message}");
        }
    }

    /// <summary>Adds a folder root to scan (no-op if blank or already present), persisting the updated
    /// set so it survives a restart even before the next scan.</summary>
    public void AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || Folders.Contains(folder))
            return;

        Folders.Add(folder);
        _ = PersistFoldersAsync();
    }

    /// <summary>Removes a scan folder (no-op if absent), persisting the trimmed set.</summary>
    public void RemoveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Folders.Remove(folder))
            return;

        _ = PersistFoldersAsync();
    }

    /// <summary>Lazily renders <paramref name="row"/>'s grid thumbnail once, off the UI thread and under a
    /// concurrency cap, then marshals it back. Called as tiles scroll into view, so only visible assets
    /// pay the decode. Best-effort: a missing renderer or an unproduceable preview just leaves the kind
    /// glyph (never a failure path, #26).</summary>
    public async Task EnsureThumbnailAsync(VisualAssetRowViewModel row)
    {
        if (row is null || _thumbnails is null)
            return;

        string path = row.Asset.File.Path;
        lock (_thumbnailRequested)
        {
            if (!_thumbnailRequested.Add(path))
                return;
        }

        await _thumbnailGate.WaitAsync().ConfigureAwait(false);
        try
        {
            VisualPreviewFrame? frame = await _thumbnails
                .RenderAsync(path, row.Asset.Kind, ThumbnailMaxEdge)
                .ConfigureAwait(false);
            if (frame is null)
                return;

            RxApp.MainThreadScheduler.Schedule(() => row.Thumbnail = ToBitmap(frame));
        }
        catch
        {
            // Thumbnail is a convenience — on any decode failure the tile keeps its glyph; allow a retry.
            lock (_thumbnailRequested)
                _thumbnailRequested.Remove(path);
        }
        finally
        {
            _thumbnailGate.Release();
        }
    }

    private async Task RunScanAsync()
    {
        IsScanning = true;
        ScanProgressValue = 0;
        // Snapshot the folder set on the calling thread so the persisted copy matches what was scanned.
        List<string> folders = Folders.ToList();
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatus = p.Total == 0 ? "No new files." : $"Probing {p.Done} / {p.Total}…";
                ScanProgressValue = p.Total == 0 ? 0 : 100.0 * p.Done / p.Total;
            });

            await _library.ScanAsync(folders, progress).ConfigureAwait(false);

            List<VisualAssetRowViewModel> rows = BuildRows();
            await PersistCatalogAsync(folders).ConfigureAwait(false);

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                _all = rows;
                ApplyFilter();
                ScanStatus = $"{rows.Count} assets";
                ScanProgressValue = 100;
            });
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Scan failed: {ex.Message}");
        }
        finally
        {
            RxApp.MainThreadScheduler.Schedule(() => IsScanning = false);
        }
    }

    // Projects the current library catalog to row view-models, title-ordered. Shared by scan + restore.
    private List<VisualAssetRowViewModel> BuildRows()
        => _library.All
            .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .Select(a => new VisualAssetRowViewModel(a))
            .ToList();

    // Re-runs the composed kind/status/text filter (pure Core VisualAssetQuery) over the catalog and
    // projects the surviving assets back to their row view-models. Suppressed during a multi-property
    // reset so ClearFilters re-queries exactly once.
    private void ApplyFilter()
    {
        if (_suppressFilter)
            return;

        var rowByAsset = _all.ToDictionary(r => r.Asset);
        var filter = new VisualAssetFilter(Text: SearchText, Kind: SelectedKind, Status: SelectedStatus);
        IReadOnlyList<VisualAsset> filtered = VisualAssetQuery.Apply(rowByAsset.Keys, filter);

        Assets.Clear();
        foreach (VisualAsset asset in filtered)
            Assets.Add(rowByAsset[asset]);
    }

    private void ClearFilters()
    {
        _suppressFilter = true;
        try
        {
            SearchText = null;
            SelectedKind = null;
            SelectedStatus = null;
        }
        finally
        {
            _suppressFilter = false;
        }

        ApplyFilter();
    }

    // Saves the catalog + scan folders. Guarded: a persistence failure surfaces on the status line but
    // never aborts a completed scan (the in-memory results are still shown).
    private async Task PersistCatalogAsync(IReadOnlyList<string> folders)
    {
        if (_store is null)
            return;

        try
        {
            await _store.SaveVisualAsync(_library.All).ConfigureAwait(false);
            await _store.SaveVisualScanFoldersAsync(folders).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Scan done; saving the catalog failed: {ex.Message}");
        }
    }

    // Persists just the folder set (after an add). Guarded so a save failure is never silent or fatal.
    private async Task PersistFoldersAsync()
    {
        if (_store is null)
            return;

        try
        {
            await _store.SaveVisualScanFoldersAsync(Folders.ToList()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not save folders: {ex.Message}");
        }
    }

    // Delete is offered only when an asset is selected AND both the file remover and the confirmation
    // prompt are wired — never expose a destructive action that cannot first confirm with the user.
    private bool CanDelete(VisualAssetRowViewModel? asset)
        => asset is not null && _fileRemover is not null && _confirmation is not null;

    // Renders (off the UI thread) the preview for the selected asset and marshals the result back. A
    // newer selection cancels this one. A missing renderer or an unproduceable preview shows a message
    // rather than a broken image — the preview is a convenience, never a failure path (#26).
    private async Task LoadPreviewAsync(VisualAssetRowViewModel? row)
    {
        _previewCts?.Cancel();

        if (row is null || _thumbnails is null)
        {
            PreviewBitmap = null;
            PreviewMessage = null;
            IsPreviewLoading = false;
            return;
        }

        var cts = new CancellationTokenSource();
        _previewCts = cts;
        VisualAsset asset = row.Asset;

        RxApp.MainThreadScheduler.Schedule(() =>
        {
            IsPreviewLoading = true;
            PreviewMessage = null;
            PreviewBitmap = null;
        });

        try
        {
            VisualPreviewFrame? frame = await _thumbnails
                .RenderAsync(asset.File.Path, asset.Kind, PreviewMaxEdge, cts.Token)
                .ConfigureAwait(false);

            RxApp.MainThreadScheduler.Schedule(() =>
            {
                if (cts.IsCancellationRequested)
                    return;

                IsPreviewLoading = false;
                if (frame is null)
                {
                    PreviewBitmap = null;
                    PreviewMessage = asset.Kind == VisualMediaKind.Video
                        ? "No preview available (FFmpeg not installed?)."
                        : "No preview available.";
                }
                else
                {
                    PreviewBitmap = ToBitmap(frame);
                    PreviewMessage = null;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection — the newer load owns the panel state.
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                if (cts.IsCancellationRequested)
                    return;
                IsPreviewLoading = false;
                PreviewBitmap = null;
                PreviewMessage = $"Preview failed: {ex.Message}";
            });
        }
    }

    // Confirms, then permanently deletes the file from disk and drops it from the catalog + persisted
    // store. A delete failure is surfaced on the status line and aborts before touching the catalog, so
    // the list never diverges from disk.
    private async Task DeleteAssetAsync(VisualAssetRowViewModel? row)
    {
        if (row is null || _fileRemover is null || _confirmation is null)
            return;

        string path = row.Asset.File.Path;
        bool confirmed = await _confirmation.ConfirmAsync(
            "Delete asset",
            $"Permanently delete this file from disk? This cannot be undone.\n\n{path}",
            "Delete").ConfigureAwait(false);
        if (!confirmed)
            return;

        try
        {
            _fileRemover.Delete(path);
        }
        catch (Exception ex)
        {
            RxApp.MainThreadScheduler.Schedule(() => ScanStatus = $"Could not delete file: {ex.Message}");
            return;
        }

        _library.Remove(path);
        _all = _all.Where(r => !string.Equals(r.Asset.File.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
        lock (_thumbnailRequested)
            _thumbnailRequested.Remove(path);

        RxApp.MainThreadScheduler.Schedule(() =>
        {
            if (ReferenceEquals(SelectedAsset, row))
                SelectedAsset = null;
            ApplyFilter();
            ScanStatus = $"Deleted {row.FileName}";
        });

        await PersistCatalogAsync(Folders.ToList()).ConfigureAwait(false);
    }

    // Copies the RGBA8 preview frame into an Avalonia bitmap for the detail panel (same RGBA8888 path
    // the live Program Out monitor uses). Runs on the UI thread.
    private static WriteableBitmap ToBitmap(VisualPreviewFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Unpremul);

        using (ILockedFramebuffer framebuffer = bitmap.Lock())
            Marshal.Copy(frame.RgbaPixels, 0, framebuffer.Address, frame.RgbaPixels.Length);

        return bitmap;
    }
}
