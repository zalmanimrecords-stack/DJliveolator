using System.Text.Json;
using Liveolator.Core.Skins;

namespace Liveolator.Media.Skins;

/// <summary>Outcome of writing a <c>.ctrlskin</c>: created with its id/path, or rejected with a reason.</summary>
public sealed record ControlSkinWriteResult(bool Created, string? SkinId, string? Path, string? Error);

/// <summary>One existing <c>.ctrlskin</c> file on disk (doc 30): its display name, derived id, kind, and path.</summary>
public sealed record ControlSkinEntry(string Name, string SkinId, string Kind, string Path);

/// <summary>
/// Writes and lists user/agent-authored control skins in the control-skins folder (doc 30) — the seam the
/// MCP server uses to add a parametric knob/slider look. Validates with <see cref="ControlSkinValidator"/>
/// before writing and derives the file name + id from the name via <see cref="ControlSkinNaming"/>, so the
/// written file lands on a predictable id the app's loader will register. Mirrors <c>FrktlPresetWriter</c>.
/// </summary>
public sealed class ControlSkinWriter
{
    public const string Extension = ".ctrlskin";

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public ControlSkinWriter(string? folder = null)
        => Folder = folder ?? Path.Combine(JsonCatalogStore.DefaultRoot(), "control-skins");

    /// <summary>The folder the <c>.ctrlskin</c> files live in (shared with the app's skin loader).</summary>
    public string Folder { get; }

    /// <summary>
    /// Validates and writes a skin as <c>&lt;slug&gt;.ctrlskin</c>. Returns the derived id and path on
    /// success, or a validation/IO error without writing. An existing file is overwritten only when
    /// <paramref name="overwrite"/> is true, so an agent does not silently clobber a hand-tuned skin.
    /// </summary>
    public ControlSkinWriteResult Write(ControlSkinFile file, bool overwrite = true)
    {
        ControlSkinValidation validation = ControlSkinValidator.Validate(file);
        if (!validation.IsValid)
            return new ControlSkinWriteResult(false, null, null, validation.Error);

        string slug = ControlSkinNaming.Slug(file.Name);
        string skinId = $"{ControlSkinNaming.PackageId}/{slug}";
        string path = Path.Combine(Folder, slug + Extension);
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(path) && !overwrite)
                return new ControlSkinWriteResult(false, skinId, path,
                    $"A skin file '{slug}{Extension}' already exists; pass overwrite=true to replace it.");

            File.WriteAllText(path, JsonSerializer.Serialize(file, WriteOptions));
            return new ControlSkinWriteResult(true, skinId, path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ControlSkinWriteResult(false, skinId, path, $"Could not write the skin file ({ex.Message}).");
        }
    }

    /// <summary>Lists the valid <c>.ctrlskin</c> files in the folder. Never throws — a bad file is skipped.</summary>
    public IReadOnlyList<ControlSkinEntry> List()
    {
        var entries = new List<ControlSkinEntry>();
        if (!Directory.Exists(Folder))
            return entries;

        try
        {
            foreach (string path in Directory
                         .EnumerateFiles(Folder, "*" + Extension, SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    ControlSkinFile? file = JsonSerializer.Deserialize<ControlSkinFile>(File.ReadAllText(path), ReadOptions);
                    if (file is null || !ControlSkinValidator.Validate(file).IsValid)
                        continue;
                    string slug = ControlSkinNaming.Slug(Path.GetFileNameWithoutExtension(path));
                    entries.Add(new ControlSkinEntry(file.Name, $"{ControlSkinNaming.PackageId}/{slug}", file.Kind, path));
                }
                catch (Exception ex) when (ex is IOException or JsonException)
                {
                    // Skip an unreadable/malformed file; listing stays best-effort.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable folder → empty list, never throw.
        }

        return entries;
    }
}
