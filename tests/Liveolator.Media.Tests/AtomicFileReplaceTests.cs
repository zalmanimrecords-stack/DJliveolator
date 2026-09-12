using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Media;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// The temp-then-replace step every store finishes its save with. Two writers replacing the SAME
/// destination — the app and the MCP server both save playlists and studio projects — lose a race
/// inside Windows' MoveFileEx and surface it as UnauthorizedAccessException, which is a save that
/// silently did not happen. The replace has to ride that out, and still fail loudly when the
/// obstruction is not transient.
/// </summary>
public sealed class AtomicFileReplaceTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "liveolator-atomic-replace", Guid.NewGuid().ToString("N"));

    public AtomicFileReplaceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Temp(string content)
    {
        string path = Path.Combine(_dir, $"{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, content);
        return path;
    }

    private string Destination(string content)
    {
        string path = Path.Combine(_dir, "state.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Replaces_the_destination_and_removes_the_temp()
    {
        string destination = Destination("old");
        string temp = Temp("new");

        await AtomicFileReplace.ReplaceAsync(temp, destination);

        Assert.Equal("new", File.ReadAllText(destination));
        Assert.False(File.Exists(temp));
    }

    [Fact]
    public async Task Creates_the_destination_when_there_is_nothing_to_replace()
    {
        string destination = Path.Combine(_dir, "state.json");
        string temp = Temp("first");

        await AtomicFileReplace.ReplaceAsync(temp, destination);

        Assert.Equal("first", File.ReadAllText(destination));
    }

    [Fact]
    public async Task Rides_out_a_destination_that_is_briefly_held()
    {
        // Exactly what the losing side of a concurrent replace sees: the destination is unavailable
        // for a moment, then free. A single attempt throws here; that is the bug being fixed.
        string destination = Destination("old");
        string temp = Temp("new");

        var holder = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using (var released = new ManualResetEventSlim(false))
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(120);
                holder.Dispose();
                released.Set();
            });

            await AtomicFileReplace.ReplaceAsync(temp, destination);

            Assert.True(released.IsSet, "the replace should not have succeeded before the holder let go");
        }

        Assert.Equal("new", File.ReadAllText(destination));
    }

    [Fact]
    public async Task Still_fails_when_the_obstruction_is_not_transient()
    {
        // A retry must not turn a permanent problem into a silent no-op: a save that never lands has
        // to reach the store's own error handling.
        string destination = Destination("old");
        string temp = Temp("new");

        var holder = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => AtomicFileReplace.ReplaceAsync(temp, destination));
        }
        finally
        {
            // Read only after letting go: the exclusive handle blocks this test as surely as the replace.
            holder.Dispose();
        }

        Assert.Equal("old", File.ReadAllText(destination));
    }

    [Fact]
    public async Task Concurrent_replacers_all_land_without_throwing()
    {
        // The production shape: many savers, one destination. Every one must complete, and the file
        // must be left as exactly one of the written values rather than torn.
        string destination = Destination("old");
        var temps = new string[12];
        for (int i = 0; i < temps.Length; i++)
            temps[i] = Temp($"value-{i}");

        await Task.WhenAll(Array.ConvertAll(
            temps, temp => Task.Run(() => AtomicFileReplace.ReplaceAsync(temp, destination))));

        string landed = File.ReadAllText(destination);
        Assert.StartsWith("value-", landed, StringComparison.Ordinal);
        Assert.All(temps, temp => Assert.False(File.Exists(temp)));
    }
}
