using System;
using System.IO;
using Liveolator.App.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Liveolator.App.Tests.Diagnostics;

public sealed class FileLoggerTests : IDisposable
{
    private readonly string _dir;
    private readonly RollingLogFile _file;

    public FileLoggerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "liveolator-filelogger-tests", Guid.NewGuid().ToString("N"));
        _file = new RollingLogFile(new FileLoggerOptions { Directory = _dir, FilePrefix = "test" });
    }

    private string ReadLog()
    {
        _file.Dispose(); // flush + release before reading
        return File.ReadAllText(Path.Combine(_dir, "test.log"));
    }

    [Fact]
    public void Log_BelowMinimumLevel_IsDropped()
    {
        ILogger logger = new FileLogger("Cat", _file, () => LogLevel.Warning);

        logger.LogInformation("info message");
        logger.LogWarning("warning message");

        string content = ReadLog();
        Assert.DoesNotContain("info message", content);
        Assert.Contains("warning message", content);
    }

    [Fact]
    public void Log_WritesCategoryLevelAndException()
    {
        ILogger logger = new FileLogger("MyCategory", _file, () => LogLevel.Information);

        logger.LogError(new InvalidOperationException("boom"), "it failed");

        string content = ReadLog();
        Assert.Contains("[Error]", content);
        Assert.Contains("MyCategory", content);
        Assert.Contains("it failed", content);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("boom", content);
    }

    [Fact]
    public void Provider_SharesOneFileAcrossCategories()
    {
        using var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = _dir, FilePrefix = "shared" });

        provider.CreateLogger("A").LogInformation("from A");
        provider.CreateLogger("B").LogInformation("from B");
        provider.Dispose();

        string content = File.ReadAllText(Path.Combine(_dir, "shared.log"));
        Assert.Contains("from A", content);
        Assert.Contains("from B", content);
    }

    public void Dispose()
    {
        _file.Dispose();
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
