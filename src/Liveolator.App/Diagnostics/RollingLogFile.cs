using System.Text;
using System.Threading.Channels;

namespace Liveolator.App.Diagnostics;

/// <summary>
/// A thread-safe append-only log file with size-based rotation. Many loggers (one per category) share
/// one instance, so entries are queued and written by ONE background writer, which is what keeps a log
/// call cheap for its caller. The write itself still flushes immediately — a crash log is only useful
/// if it survived the crash. When the active file passes
/// <see cref="FileLoggerOptions.MaxFileBytes"/> it is rolled to <c>{prefix}.1.log</c> (shifting older
/// files up) and the oldest beyond <see cref="FileLoggerOptions.MaxRetainedFiles"/> is pruned.
/// </summary>
/// <remarks>
/// <para>
/// Logging must never take down the app: every IO failure is swallowed to <see cref="System.Diagnostics.Trace"/>
/// rather than thrown. Disposed writers stop accepting writes silently.
/// </para>
/// <para>
/// <b>Why the queue.</b> Appending used to write and flush to disk under a process-wide lock on the
/// CALLER's thread. Raise the verbosity to Debug and the mixer's DSP path logs every buffer — roughly a
/// hundred flushed disk writes a second, from the audio thread, contending with the UI thread's own
/// logging. Turning on logging to diagnose a problem would cause a dropout, which is a cruel way to
/// lose a set. Handing the entry to a channel costs the caller an enqueue and nothing else.
/// </para>
/// <para>
/// The trade is bounded and deliberate: entries still sitting in the queue when the process is killed
/// outright are lost, where before every returned call was already on disk. Dispose drains first, so an
/// orderly shutdown loses nothing. The queue is capped and drops its OLDEST entries when a burst
/// overruns it, because in a storm the newest lines are the ones describing what went wrong.
/// </para>
/// </remarks>
public sealed class RollingLogFile : IDisposable
{
    // Deep enough to swallow a burst (a stack trace per category, a scan's worth of progress), small
    // enough that a runaway logger cannot eat memory. Oldest-out, so a storm keeps its tail.
    private const int QueueCapacity = 4096;
    // Bounded so a wedged disk cannot hold shutdown open; the watchdog's budget is already tight.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly FileLoggerOptions _options;
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    private readonly Task _pump;
    private StreamWriter? _writer;
    private long _length;
    private bool _disposed;

    public RollingLogFile(FileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pump = Task.Run(DrainAsync);
    }

    /// <summary>The absolute path of the active log file.</summary>
    public string CurrentFilePath => _options.CurrentFilePath;

    /// <summary>
    /// Queues one entry (a single line, may contain embedded newlines) for the background writer.
    /// Returns as soon as it is queued — safe to call from the audio thread.
    /// </summary>
    public void Append(string entry)
    {
        if (entry is null || _disposed)
            return;

        _queue.Writer.TryWrite(entry);
    }

    // The single background writer. Exceptions cannot escape here or the pump dies silently and the log
    // stops for the rest of the run, so WriteEntry swallows IO the same way the caller-side used to.
    private async Task DrainAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
                while (_queue.Reader.TryRead(out string? entry))
                    WriteEntry(entry);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Liveolator file log pump stopped: {ex.Message}");
        }
    }

    private void WriteEntry(string entry)
    {
        lock (_gate)
        {
            // Deliberately NOT gated on _disposed: Dispose sets that flag before draining, so checking it
            // here would throw away exactly the entries the drain exists to save. Append is the gate that
            // stops new work; the closed channel is what stops this loop.
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
        if (_disposed)
            return;
        _disposed = true;

        // Let the pump finish what is already queued before the file closes, so an orderly shutdown
        // keeps its last lines — but never longer than DrainTimeout, because the shutdown watchdog is
        // waiting and a stuck disk must not be the thing that kills the app.
        _queue.Writer.TryComplete();
        try
        {
            _pump.Wait(DrainTimeout);
        }
        catch (AggregateException)
        {
            // The pump already reported its own failure; disposal continues regardless.
        }

        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
