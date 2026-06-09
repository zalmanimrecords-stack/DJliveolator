using System.Diagnostics;
using System.Text.Json;

namespace Liveolator.Mcp.Tests;

public sealed class StdioSmokeTests
{
    [Fact]
    public async Task InitializeThenListTools_ReturnsTheServerToolCatalog()
    {
        string serverAssembly = typeof(Liveolator.Mcp.Tools.LibraryTools).Assembly.Location;
        string dataDirectory = Path.Combine(Path.GetTempPath(), $"liveolator-mcp-smoke-{Guid.NewGuid():N}");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet")
            {
                Arguments = $"\"{serverAssembly}\" --stdio --data \"{dataDirectory}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        try
        {
            Assert.True(process.Start());
            _ = process.StandardError.ReadToEndAsync();

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { },
                    clientInfo = new { name = "liveolator-tests", version = "1.0" },
                },
            });

            using JsonDocument initialize = await ReadResponseAsync(process);
            Assert.Equal("2025-03-26", initialize.RootElement
                .GetProperty("result")
                .GetProperty("protocolVersion")
                .GetString());

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized",
                @params = new { },
            });
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list",
                @params = new { },
            });

            using JsonDocument toolsResponse = await ReadResponseAsync(process);
            string[] names = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()!)
                .ToArray();

            Assert.Contains("list_tracks", names);
            Assert.Contains("reanalyze_pending_tracks", names);
            Assert.Contains("scan_visual_folders", names);

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = "compatible_keys",
                    arguments = new { camelot = "8B" },
                },
            });

            using JsonDocument callResponse = await ReadResponseAsync(process);
            Assert.False(callResponse.RootElement.TryGetProperty("error", out _));
            JsonElement callResult = callResponse.RootElement.GetProperty("result");
            Assert.False(callResult.TryGetProperty("isError", out JsonElement isError)
                         && isError.GetBoolean());
            Assert.Contains("8B", callResult.GetRawText());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static async Task SendAsync(Process process, object message)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonDocument> ReadResponseAsync(Process process)
    {
        string? line = await process.StandardOutput.ReadLineAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(string.IsNullOrWhiteSpace(line));
        return JsonDocument.Parse(line);
    }
}
