using System.Text.Json;
using Liveolator.Core.Skins;

namespace Liveolator.Media.Skins;

/// <summary>One control skin loaded from disk (doc 30): its derived id and the full validated file.</summary>
public sealed record LoadedControlSkin(string SkinId, ControlSkinFile File);

/// <summary>
/// Loads user/agent-authored control skins (<c>*.ctrlskin</c>, doc 30) from the shared control-skins folder
/// so the app can list them in a picker and apply the chosen palette. Tolerant by design (doc 21): a missing
/// folder yields zero skins, and a malformed or invalid file is skipped + reported via <c>onWarning</c>
/// rather than aborting the rest (global standards #16/#26). Reads full files (unlike
/// <see cref="ControlSkinWriter.List"/>, which returns summaries) because applying a skin needs its colours.
/// </summary>
public sealed class ControlSkinFolderLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Action<string>? _onWarning;

    public ControlSkinFolderLoader(string? folder = null, Action<string>? onWarning = null)
    {
        Folder = folder ?? Path.Combine(JsonCatalogStore.DefaultRoot(), "control-skins");
        _onWarning = onWarning;
    }

    /// <summary>The folder scanned for <c>*.ctrlskin</c> files (shared with the MCP authoring session).</summary>
    public string Folder { get; }

    /// <summary>Scans and returns every valid control skin in the folder. Never throws.</summary>
    public IReadOnlyList<LoadedControlSkin> Load()
    {
        var skins = new List<LoadedControlSkin>();
        if (!Directory.Exists(Folder))
            return skins;

        try
        {
            foreach (string path in Directory
                         .EnumerateFiles(Folder, "*" + ControlSkinWriter.Extension, SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                TryLoadOne(path, skins);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onWarning?.Invoke($"Control-skin folder '{Folder}' could not be scanned ({ex.Message}).");
        }

        return skins;
    }

    private void TryLoadOne(string path, List<LoadedControlSkin> skins)
    {
        string fileName = Path.GetFileName(path);
        try
        {
            ControlSkinFile? file = JsonSerializer.Deserialize<ControlSkinFile>(File.ReadAllText(path), JsonOptions);
            ControlSkinValidation validation = ControlSkinValidator.Validate(file);
            if (!validation.IsValid)
            {
                _onWarning?.Invoke($"Control skin '{fileName}' was skipped ({validation.Error}).");
                return;
            }

            string slug = ControlSkinNaming.Slug(Path.GetFileNameWithoutExtension(path));
            skins.Add(new LoadedControlSkin($"{ControlSkinNaming.PackageId}/{slug}", file!));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _onWarning?.Invoke($"Control skin '{fileName}' was skipped ({ex.Message}).");
        }
    }
}
