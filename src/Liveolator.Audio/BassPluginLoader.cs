using ManagedBass;

namespace Liveolator.Audio;

internal static class BassPluginLoader
{
    // PluginLoad probes the app directory for the add-on dll under its platform name; a 0 handle (not
    // present) is fine — the format just stays unsupported and the caller falls back / logs. Process-global
    // and idempotent, so load order across callers does not matter.
    public static bool TryLoad(string baseName)
    {
        try
        {
            return Bass.PluginLoad(baseName) != 0
                || Bass.PluginLoad($"{baseName}.dll") != 0
                || Bass.PluginLoad($"lib{baseName}.so") != 0
                || Bass.PluginLoad($"lib{baseName}.dylib") != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
