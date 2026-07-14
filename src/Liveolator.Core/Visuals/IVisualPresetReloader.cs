namespace Liveolator.Core.Visuals;

/// <summary>
/// Re-scans the user FRKTL preset folder at runtime and republishes the result into the effect + preset
/// registries (doc 29). Lets the LIVE surface pick up presets authored while the app is running (e.g. via
/// the MCP server) without an app restart. Implementations must be tolerant: a missing folder or an
/// unreadable file is skipped, never thrown (global standards #16/#26).
/// </summary>
public interface IVisualPresetReloader
{
    /// <summary>Re-scans the folder, replaces the package's registrations, and returns the preset count.</summary>
    int Reload();
}
