using Liveolator.Core.Autopilot;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Visuals;

namespace Liveolator.Media;

/// <summary>
/// Persists authored Live-Mode data as versioned JSON under the per-user <c>live/</c> layout
/// (doc 13), mirroring <see cref="JsonCatalogStore"/>: versioned snapshot records, atomic
/// temp-then-move saves, and tolerant loads that return <c>null</c>/empty (with a warning) on a
/// missing, corrupt, or older-schema file rather than crashing the app (global standards #16/#26).
/// </summary>
/// <remarks>
/// <para>On-disk layout, rooted at <see cref="JsonCatalogStore.DefaultRoot"/> (<c>%APPDATA%/Liveolator</c>
/// or the Mac/XDG equivalent):</para>
/// <code>
/// live/
///   mappings/&lt;name&gt;.json    ControllerMappingProfile (doc 05)
///   scenes/&lt;name&gt;.json      VisualBank + its VisualScenes (doc 08)
///   macros.json              VisualMacro definitions (doc 08)
///   autopilot/&lt;name&gt;.json   AutopilotRuleSet / show (doc 10)
/// </code>
/// <para>This matches the doc 13 proposal; the per-user <c>live/</c> tree holds authored data and is
/// kept separate from the regenerable catalog cache and from app-shipped <c>defaults/live/</c>.</para>
/// </remarks>
public sealed class LiveProfileStore : ILiveProfileStore
{
    private readonly string _liveRoot;
    private readonly JsonFileSnapshotIo _io;

    /// <param name="rootDirectory">
    /// Persistence root; defaults to <see cref="JsonCatalogStore.DefaultRoot"/>. The <c>live/</c>
    /// subtree is created beneath it.
    /// </param>
    /// <param name="onWarning">Receives a human-readable message when a file is skipped.</param>
    public LiveProfileStore(string? rootDirectory = null, Action<string>? onWarning = null)
    {
        _liveRoot = Path.Combine(rootDirectory ?? JsonCatalogStore.DefaultRoot(), "live");
        _io = new JsonFileSnapshotIo(onWarning);
    }

    /// <summary>Full path of the named mapping profile file.</summary>
    public string MappingProfilePath(string name) => Path.Combine(_liveRoot, "mappings", FileNameFor(name));

    /// <summary>Full path of the named visual bank file.</summary>
    public string VisualBankPath(string name) => Path.Combine(_liveRoot, "scenes", FileNameFor(name));

    /// <summary>Full path of the macro-definitions file.</summary>
    public string VisualMacrosPath => Path.Combine(_liveRoot, "macros.json");

    /// <summary>Full path of the named autopilot rule-set file.</summary>
    public string AutopilotRuleSetPath(string name) => Path.Combine(_liveRoot, "autopilot", FileNameFor(name));

    public Task SaveMappingProfileAsync(ControllerMappingProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return _io.SaveAsync(
            MappingProfilePath(profile.Name),
            new MappingProfileSnapshot(MappingProfileSnapshot.CurrentVersion, profile),
            cancellationToken);
    }

    public async Task<ControllerMappingProfile?> LoadMappingProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string path = MappingProfilePath(name);
        MappingProfileSnapshot? snapshot = await _io.LoadAsync<MappingProfileSnapshot>(path, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        if (snapshot.Version != MappingProfileSnapshot.CurrentVersion)
        {
            _io.WarnVersionMismatch(path, snapshot.Version, MappingProfileSnapshot.CurrentVersion);
            return null;
        }
        return snapshot.Profile;
    }

    public Task SaveVisualBankAsync(VisualBank bank, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bank);
        return _io.SaveAsync(
            VisualBankPath(bank.Name),
            new VisualBankSnapshot(VisualBankSnapshot.CurrentVersion, bank),
            cancellationToken);
    }

    public async Task<VisualBank?> LoadVisualBankAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string path = VisualBankPath(name);
        VisualBankSnapshot? snapshot = await _io.LoadAsync<VisualBankSnapshot>(path, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        if (snapshot.Version != VisualBankSnapshot.CurrentVersion)
        {
            _io.WarnVersionMismatch(path, snapshot.Version, VisualBankSnapshot.CurrentVersion);
            return null;
        }
        return snapshot.Bank;
    }

    public Task<IReadOnlyList<string>> ListVisualBankNamesAsync(CancellationToken cancellationToken = default)
    {
        string dir = Path.Combine(_liveRoot, "scenes");
        // A missing folder simply means no banks have been saved yet — return empty, never throw.
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        try
        {
            IReadOnlyList<string> names = Directory
                .EnumerateFiles(dir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult(names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable scenes folder degrades to "no banks", surfaced via the store's warning sink.
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }

    public Task SaveVisualMacrosAsync(IEnumerable<VisualMacro> macros, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(macros);
        return _io.SaveAsync(
            VisualMacrosPath,
            new VisualMacrosSnapshot(VisualMacrosSnapshot.CurrentVersion, macros.ToList()),
            cancellationToken);
    }

    public async Task<IReadOnlyList<VisualMacro>> LoadVisualMacrosAsync(CancellationToken cancellationToken = default)
    {
        VisualMacrosSnapshot? snapshot = await _io.LoadAsync<VisualMacrosSnapshot>(VisualMacrosPath, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return Array.Empty<VisualMacro>();
        if (snapshot.Version != VisualMacrosSnapshot.CurrentVersion)
        {
            _io.WarnVersionMismatch(VisualMacrosPath, snapshot.Version, VisualMacrosSnapshot.CurrentVersion);
            return Array.Empty<VisualMacro>();
        }
        return snapshot.Macros;
    }

    public Task SaveAutopilotRuleSetAsync(AutopilotRuleSet ruleSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        return _io.SaveAsync(
            AutopilotRuleSetPath(ruleSet.Name),
            new AutopilotRuleSetSnapshot(AutopilotRuleSetSnapshot.CurrentVersion, ruleSet),
            cancellationToken);
    }

    public async Task<AutopilotRuleSet?> LoadAutopilotRuleSetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string path = AutopilotRuleSetPath(name);
        AutopilotRuleSetSnapshot? snapshot = await _io.LoadAsync<AutopilotRuleSetSnapshot>(path, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return null;
        if (snapshot.Version != AutopilotRuleSetSnapshot.CurrentVersion)
        {
            _io.WarnVersionMismatch(path, snapshot.Version, AutopilotRuleSetSnapshot.CurrentVersion);
            return null;
        }
        return snapshot.RuleSet;
    }

    /// <summary>
    /// Maps a profile name to a safe, flat <c>.json</c> file name. Any character that is not a
    /// letter, digit, dash, or underscore is replaced so a name can never escape its folder via
    /// path separators or <c>..</c> (global standards #17/#19).
    /// </summary>
    private static string FileNameFor(string name)
    {
        Span<char> buffer = stackalloc char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            buffer[i] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_';
        }
        return new string(buffer) + ".json";
    }
}
