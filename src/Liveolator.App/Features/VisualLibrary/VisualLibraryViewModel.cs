using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Liveolator.App.Shell;
using Liveolator.Core.Library;
using Liveolator.Core.Library.Visual;
using Liveolator.Core.Persistence;
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
    private readonly VisualMediaLibrary _library;
    private readonly IVisualCatalogStore? _store;
    private List<VisualAssetRowViewModel> _all = new();

    private string? _searchText;
    private VisualMediaKind? _selectedKind;
    private MediaAnalysisStatus? _selectedStatus;
    private bool _suppressFilter;
    private VisualAssetRowViewModel? _selectedAsset;
    private string _scanStatus = "Add folders, then Scan.";
    private bool _isScanning;
    private double _scanProgressValue;

    /// <param name="library">The Core visual-media library (scan/catalog).</param>
    /// <param name="store">Persists the catalog + scan folders across runs; null disables persistence
    /// (the tab still works in-memory for the session).</param>
    public VisualLibraryViewModel(VisualMediaLibrary library, IVisualCatalogStore? store = null)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _store = store;

        ScanCommand = ReactiveCommand.CreateFromTask(
            RunScanAsync,
            this.WhenAnyValue(x => x.IsScanning, scanning => !scanning));
        ClearFiltersCommand = ReactiveCommand.Create(ClearFilters);

        Observable.Merge(
                this.WhenAnyValue(x => x.SearchText).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedKind).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedStatus).Select(_ => Unit.Default))
            .Subscribe(_ => ApplyFilter());
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
}
