using System.Text.Json;
using Liveolator.Core.Visuals;

namespace Liveolator.Media.Visuals;

/// <summary>Outcome of writing a <c>.frktl</c> preset: created with its id/path, or rejected with a reason.</summary>
public sealed record FrktlPresetWriteResult(bool Created, string? PresetId, string? Path, string? Error);

/// <summary>One existing <c>.frktl</c> file on disk (doc 29): its display name, derived preset id, and path.</summary>
public sealed record FrktlPresetEntry(string Name, string PresetId, string Path);

/// <summary>
/// Writes and lists user-authored <c>.frktl</c> presets in the FRKTL presets folder (doc 29) — the seam an
/// agent or the UI uses to add a preset. Validates with <see cref="FrktlPresetValidator"/> before writing,
/// and derives the file name + preset id from the name via <see cref="FrktlPresetNaming"/> so the written
/// file is exactly what <see cref="FrktlPresetFolderLoader"/> will register.
/// </summary>
public sealed class FrktlPresetWriter
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public FrktlPresetWriter(string? folder = null)
        => Folder = folder ?? Path.Combine(JsonCatalogStore.DefaultRoot(), "frktl-presets");

    /// <summary>The folder the <c>.frktl</c> files live in (shared with <see cref="FrktlPresetFolderLoader"/>).</summary>
    public string Folder { get; }

    /// <summary>
    /// Validates and writes a preset as <c>&lt;slug&gt;.frktl</c>. Returns the derived preset id and path on
    /// success, or a validation/IO error without writing. An existing file is overwritten only when
    /// <paramref name="overwrite"/> is true, so an agent does not silently clobber a hand-tuned preset.
    /// </summary>
    public FrktlPresetWriteResult Write(FrktlPresetFile file, bool overwrite = true)
    {
        FrktlPresetValidation validation = FrktlPresetValidator.Validate(file);
        if (!validation.IsValid)
            return new FrktlPresetWriteResult(false, null, null, validation.Error);

        string slug = FrktlPresetNaming.Slug(file.Name);
        string presetId = $"{FrktlPresetFolderLoader.PackageId}/{slug}";
        string path = Path.Combine(Folder, slug + ".frktl");
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(path) && !overwrite)
                return new FrktlPresetWriteResult(false, presetId, path,
                    $"A preset file '{slug}.frktl' already exists; pass overwrite=true to replace it.");

            File.WriteAllText(path, JsonSerializer.Serialize(file, WriteOptions));
            return new FrktlPresetWriteResult(true, presetId, path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FrktlPresetWriteResult(false, presetId, path, $"Could not write the preset file ({ex.Message}).");
        }
    }

    /// <summary>Lists the valid <c>.frktl</c> files currently in the folder. Never throws — a bad file is skipped.</summary>
    public IReadOnlyList<FrktlPresetEntry> List()
    {
        var entries = new List<FrktlPresetEntry>();
        if (!Directory.Exists(Folder))
            return entries;

        try
        {
            foreach (string path in Directory
                         .EnumerateFiles(Folder, "*.frktl", SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    FrktlPresetFile? file = JsonSerializer.Deserialize<FrktlPresetFile>(File.ReadAllText(path), ReadOptions);
                    if (file is null || !FrktlPresetValidator.Validate(file).IsValid)
                        continue;
                    string slug = FrktlPresetNaming.Slug(Path.GetFileNameWithoutExtension(path));
                    entries.Add(new FrktlPresetEntry(file.Name, $"{FrktlPresetFolderLoader.PackageId}/{slug}", path));
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
