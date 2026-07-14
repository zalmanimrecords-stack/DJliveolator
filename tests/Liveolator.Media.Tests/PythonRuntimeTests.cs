using System;
using System.IO;
using Liveolator.Media.Analysis;
using Xunit;

namespace Liveolator.Media.Tests;

public class PythonRuntimeTests
{
    [Fact]
    public void InterpreterPath_PointsIntoPerUserPythonDir()
    {
        string path = new PythonRuntime().InterpreterPath;
        Assert.Contains("Liveolator", path);
        Assert.Contains("python", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsAvailable_False_WhenInterpreterMissing()
    {
        var runtime = new PythonRuntime(baseDir: Path.Combine(Path.GetTempPath(), "liveolator-no-such-python-" + Guid.NewGuid()));
        Assert.False(runtime.IsAvailable);
    }

    [Fact]
    public void IsAvailable_True_WhenInterpreterPresent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "liveolator-python-" + Guid.NewGuid());
        try
        {
            var runtime = new PythonRuntime(baseDir: dir);
            Directory.CreateDirectory(Path.GetDirectoryName(runtime.InterpreterPath)!);
            File.WriteAllText(runtime.InterpreterPath, "stub");
            Assert.True(runtime.IsAvailable);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
