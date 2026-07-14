using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.Media.Analysis;
using Xunit;

namespace Liveolator.Media.Tests;

/// <summary>
/// Regression guard for the .tar.gz extraction (doc 32 §2.1 advanced-analysis download). Extracting a
/// python-build-standalone archive straight from a non-seekable GZipStream mangled entry names (e.g.
/// "python.exe" written as "python.exe_hon.exe"), leaving the interpreter absent at its expected path and
/// failing the whole install. Extraction must reproduce the exact names and contents.
/// </summary>
public sealed class RealPythonRuntimeOpsExtractTests
{
    [Fact]
    public async Task ExtractAsync_ReproducesExactEntryNamesAndContents()
    {
        string root = Path.Combine(Path.GetTempPath(), "liveolator-extract-" + Path.GetRandomFileName());
        string archive = Path.Combine(root, "python.tar.gz");
        string destDir = Path.Combine(root, "out");
        Directory.CreateDirectory(root);

        try
        {
            var entries = new (string Name, string Content)[]
            {
                ("python/python.exe", "interpreter-bytes"),
                ("python/LICENSE.txt", "license-text"),
                ("python/Lib/os.py", "import sys"),
            };
            WriteTarGz(archive, entries);

            bool ok = await new RealPythonRuntimeOps().ExtractAsync(archive, destDir, CancellationToken.None);

            Assert.True(ok);
            foreach ((string name, string content) in entries)
            {
                string path = Path.Combine(destDir, name.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), $"missing extracted file: {name}");
                Assert.Equal(content, File.ReadAllText(path));
            }

            // The exact interpreter path the installer later launches must exist — not a mangled sibling.
            Assert.True(File.Exists(Path.Combine(destDir, "python", "python.exe")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void WriteTarGz(string path, (string Name, string Content)[] entries)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
        foreach ((string name, string content) in entries)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name);
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            entry.DataStream = new MemoryStream(bytes);
            tar.WriteEntry(entry);
        }
    }
}
