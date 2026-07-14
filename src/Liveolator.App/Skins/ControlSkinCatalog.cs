using Liveolator.Core.Skins;
using Liveolator.Media.Skins;

namespace Liveolator.App.Skins;

/// <summary>The control skins available this session (doc 30), loaded from the control-skins folder at startup.</summary>
public interface IControlSkinCatalog
{
    IReadOnlyList<LoadedControlSkin> Skins { get; }

    /// <summary>Finds a loaded skin by its id; false if no skin with that id is installed.</summary>
    bool TryGet(string id, out ControlSkinFile skin);
}

/// <summary>
/// In-memory snapshot of the control skins found on disk at startup (doc 30). Read by the Settings pickers
/// (to list choices) and by app startup (to resolve the persisted selection). Immutable; a rescan rebuilds
/// the catalog rather than mutating it, mirroring how the UI theme manager publishes a fresh set.
/// </summary>
public sealed class ControlSkinCatalog : IControlSkinCatalog
{
    private readonly Dictionary<string, ControlSkinFile> _byId;

    public ControlSkinCatalog(IReadOnlyList<LoadedControlSkin> skins)
    {
        ArgumentNullException.ThrowIfNull(skins);
        Skins = skins;
        _byId = new Dictionary<string, ControlSkinFile>(StringComparer.Ordinal);
        foreach (LoadedControlSkin skin in skins)
            _byId[skin.SkinId] = skin.File;
    }

    public IReadOnlyList<LoadedControlSkin> Skins { get; }

    public bool TryGet(string id, out ControlSkinFile skin)
    {
        if (id is not null && _byId.TryGetValue(id, out ControlSkinFile? found))
        {
            skin = found;
            return true;
        }
        skin = null!;
        return false;
    }
}
