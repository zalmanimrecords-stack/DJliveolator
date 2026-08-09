using System.Reflection;
using ModelContextProtocol.Server;

namespace Liveolator.Mcp.Tests;

public sealed class ToolDiscoveryTests
{
    [Fact]
    public void AssemblyExposesExpectedStableToolNames()
    {
        Assembly assembly = typeof(Liveolator.Mcp.Tools.LibraryTools).Assembly;
        string[] names = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("scan_music_folders", names);
        Assert.Contains("list_tracks", names);
        Assert.Contains("reanalyze_track", names);
        Assert.Contains("reanalyze_pending_tracks", names);
        Assert.Contains("set_track_analysis", names);
        Assert.Contains("scan_visual_folders", names);
        Assert.Contains("build_harmonic_playlist", names);
        Assert.Contains("get_control_skin_spec", names);
        Assert.Contains("create_control_skin", names);
        Assert.Contains("list_control_skins", names);
        Assert.Contains("build_dj_set", names);
        Assert.Contains("list_dj_sets", names);
        Assert.Contains("get_dj_set", names);
        Assert.Contains("render_set_preview", names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }
}
