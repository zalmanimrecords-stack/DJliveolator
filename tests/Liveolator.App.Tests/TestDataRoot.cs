using System;
using System.IO;
using System.Runtime.CompilerServices;
using Liveolator.Media;

namespace Liveolator.App.Tests;

/// <summary>
/// Points every default persistence root at a per-run temp folder before any test runs. The headless
/// harness boots the real App, whose composition root falls back to the default root; without this a
/// test run wrote into the user's real catalog.
/// </summary>
internal static class TestDataRoot
{
    [ModuleInitializer]
    internal static void Isolate()
    {
        string root = Path.Combine(Path.GetTempPath(), $"liveolator-app-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable(JsonCatalogStore.DataDirectoryVariable, root);
    }
}
