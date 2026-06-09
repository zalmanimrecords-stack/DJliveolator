using System;
using System.IO;
using System.Linq;
using Liveolator.App.Diagnostics;
using Xunit;

namespace Liveolator.App.Tests.Diagnostics;

public sealed class RollingLogFileTests : IDisposable
{
    private readonly string _dir;

    public RollingLogFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "liveolator-log-tests", Guid.NewGuid().ToString("N"));
    }

    private FileLoggerOptions Options(long maxBytes = 1024, int retained = 2) => new()
    {
        Directory = _dir,
        FilePrefix = "test",
        MaxFileBytes = maxBytes,
        MaxRetainedFiles = retained,
    };

    [Fact]
    public void Append_WritesEntriesToTheActiveFile()
    {
        using (var file = new RollingLogFile(Options()))
        {
            file.Append("first line");
            file.Append("second line");
        }

        string active = Path.Combine(_dir, "test.log");
        string content = File.ReadAllText(active);
        Assert.Contains("first line", content);
        Assert.Contains("second line", content);
    }

    [Fact]
    public void Append_RollsTheFileOnceItExceedsTheSizeLimit()
    {
        using var file = new RollingLogFile(Options(maxBytes: 40, retained: 3));

        // Each entry comfortably exceeds 40 bytes, so every write rolls the previous active file.
        file.Append(new string('a', 60));
        file.Append(new string('b', 60));
        file.Append(new string('c', 60));

        Assert.True(File.Exists(Path.Combine(_dir, "test.log")));   // fresh active file
        Assert.True(File.Exists(Path.Combine(_dir, "test.1.log"))); // most recent roll
    }

    [Fact]
    public void Append_PrunesRolledFilesBeyondTheRetentionLimit()
    {
        using var file = new RollingLogFile(Options(maxBytes: 40, retained: 2));

        for (int i = 0; i < 8; i++)
            file.Append(new string((char)('a' + i), 60));

        int numbered = Directory.GetFiles(_dir, "test.*.log").Length;
        Assert.True(numbered <= 2, $"Expected at most 2 rolled files, found {numbered}.");
    }

    [Fact]
    public void Append_AfterDispose_IsANoOp()
    {
        var file = new RollingLogFile(Options());
        file.Append("before dispose");
        file.Dispose();

        file.Append("after dispose"); // must not throw

        string content = File.ReadAllText(Path.Combine(_dir, "test.log"));
        Assert.DoesNotContain("after dispose", content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; a held handle on a CI box should not fail the test.
        }
    }
}
