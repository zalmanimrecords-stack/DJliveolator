using System.Text;

namespace Liveolator.App.Diagnostics;

/// <summary>
/// A thread-safe append-only log file with size-based rotation. Many loggers (one per category) share
/// one instance, so all writes are serialized under a single lock and flushed immediately — a crash
/// log is only useful if it survived the crash. When the active file passes
/// <see cref="FileLoggerOptions.MaxFileBytes"/> it is rolled to <c>{prefix}.1.log</c> (shifting older
/// files up) and the oldest beyond <see cref="FileLoggerOptions.MaxRetainedFiles"/> is pruned.
/// </summary>
/// <remarks>
/// Logging must never take down the app: every IO failure is swallowed to <see cref="System.Diagnostics.Trace"/>
/// rather than thrown. Disposed writers stop accepting writes silently.
/// </remarks>
public sealed class RollingLogFile : IDisposable
{
    private readonly object _gate = new();
    private readonly FileLoggerOptions _options;
    private StreamWriter? _writer;
    private long _length;
    private bool _disposed;

    public RollingLogFile(FileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The absolute path of the active log file.</summary>
    public string CurrentFilePath => _options.CurrentFilePath;

    /// <summary>Appends one entry (a single line, may contain embedded newlines) followed by a line break.</summary>
    public void Append(string entry)
    {
        if (entry is null)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;
            try
            {
                long entryBytes = Encoding.UTF8.GetByteCount(entry) + Environment.NewLine.Length;
                EnsureWriter();

                // Roll before the write so the active file always holds the newest entries and never
                // grows unbounded. A lone entry bigger than the limit is still written (we never split an
                // entry); the next write rolls it. Never roll an empty file.
                if (_length > 0 && _length + entryBytes > _options.MaxFileBytes)
                {
                    Roll();
                    EnsureWriter();
                }

                _writer!.Write(entry);
                _writer.Write(Environment.NewLine);
                _length += entryBytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                // Never let logging crash the app; fall back to the platform trace listener.
                System.Diagnostics.Trace.TraceError($"Liveolator file log write failed: {ex.Message}");
            }
        }
    }

    private StreamWriter EnsureWriter()
    {
        if (_writer is not null)
            return _writer;

        System.IO.Directory.CreateDirectory(_options.Directory);
        string path = _options.CurrentFilePath;
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true, // durability over throughput: crash logs must reach disk
        };
        _length = stream.Length;
        return _writer;
    }

    // Closes the active file, shifts {prefix}.N.log up (pruning the oldest), then reopens a fresh file.
    private void Roll()
    {
        _writer?.Dispose();
        _writer = null;
        _length = 0;

        string Numbered(int n) => Path.Combine(_options.Directory, $"{_options.FilePrefix}.{n}.log");
        string active = _options.CurrentFilePath;

        try
        {
            string oldest = Numbered(_options.MaxRetainedFiles);
            if (File.Exists(oldest))
                File.Delete(oldest);

            for (int n = _options.MaxRetainedFiles - 1; n >= 1; n--)
            {
                string from = Numbered(n);
                if (File.Exists(from))
                    File.Move(from, Numbered(n + 1), overwrite: true);
            }

            if (_options.MaxRetainedFiles >= 1 && File.Exists(active))
                File.Move(active, Numbered(1), overwrite: true);
            else if (File.Exists(active))
                File.Delete(active); // retain nothing: just truncate the history
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError($"Liveolator log rotation failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
