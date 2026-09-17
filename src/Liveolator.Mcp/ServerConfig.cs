using Liveolator.Audio;
using Liveolator.Media;

namespace Liveolator.Mcp;

/// <summary>Transport the server exposes.</summary>
public enum ServerMode
{
    /// <summary>stdin/stdout — for a locally-launched agent (Claude Desktop/Code).</summary>
    Stdio,

    /// <summary>HTTP/SSE on a loopback port — for remote/already-running agents.</summary>
    Http
}

/// <summary>
/// Resolved server configuration from command-line args + environment. Parsing is total and
/// throws on malformed input so a bad launch fails fast with a clear message.
/// </summary>
public sealed class ServerConfig
{
    public ServerMode Mode { get; init; } = ServerMode.Stdio;
    public int Port { get; init; } = 5174;

    /// <summary>
    /// Address the HTTP transport binds. Defaults to loopback, because this transport has no
    /// authentication of its own and must never be reachable from the network by accident.
    /// </summary>
    /// <remarks>
    /// A container is the case that needs to override it: binding the CONTAINER's loopback makes the
    /// server unreachable through a published port, since Docker forwards to the container's network
    /// interface. Bind 0.0.0.0 there and let Docker publish to 127.0.0.1 on the host - the isolation
    /// then comes from the port mapping instead of from this value, and is just as strict.
    /// </remarks>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>Path to (or bare name of) the FFmpeg executable, or null to use
    /// <c>LIVEOLATOR_FFMPEG_PATH</c>/PATH.</summary>
    public string? FfmpegPath { get; init; }

    /// <summary>Catalog-cache directory, or null for the default app-data root.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>GetSongBPM API key for online BPM/key enrichment (doc 16); null disables enrichment.</summary>
    public string? GetSongBpmKey { get; init; }

    /// <summary>AcoustID client key for fingerprint identification (doc 16); null = tag-based lookup only.</summary>
    public string? AcoustIdKey { get; init; }

    /// <summary>Path to (or bare name of) the Chromaprint <c>fpcalc</c> executable; null = PATH/env.</summary>
    public string? FpcalcPath { get; init; }

    public static ServerConfig Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var mode = ServerMode.Stdio;
        int port = 5174;
        string bind = Environment.GetEnvironmentVariable("LIVEOLATOR_BIND") ?? "127.0.0.1";
        string? ffmpegPath = Environment.GetEnvironmentVariable(FfmpegOptions.EnvironmentVariable);
        string? dataDir = Environment.GetEnvironmentVariable("LIVEOLATOR_DATA");
        string? getSongBpmKey = Environment.GetEnvironmentVariable("LIVEOLATOR_GETSONGBPM_KEY");
        string? acoustIdKey = Environment.GetEnvironmentVariable("LIVEOLATOR_ACOUSTID_KEY");
        string? fpcalcPath = Environment.GetEnvironmentVariable("LIVEOLATOR_FPCALC_PATH");

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stdio":
                    mode = ServerMode.Stdio;
                    break;
                case "--http":
                    mode = ServerMode.Http;
                    break;
                case "--port":
                    port = RequireInt(args, ref i, "--port");
                    break;
                case "--bind":
                    bind = RequireValue(args, ref i, "--bind");
                    break;
                case "--ffmpeg":
                    ffmpegPath = RequireValue(args, ref i, "--ffmpeg");
                    break;
                case "--data":
                    dataDir = RequireValue(args, ref i, "--data");
                    break;
                case "--getsongbpm-key":
                    getSongBpmKey = RequireValue(args, ref i, "--getsongbpm-key");
                    break;
                case "--acoustid-key":
                    acoustIdKey = RequireValue(args, ref i, "--acoustid-key");
                    break;
                case "--fpcalc":
                    fpcalcPath = RequireValue(args, ref i, "--fpcalc");
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown argument '{args[i]}'. Valid: --stdio | --http [--port N] [--bind ADDR] [--ffmpeg PATH] "
                        + "[--data DIR] [--getsongbpm-key KEY] [--acoustid-key KEY] [--fpcalc PATH].");
            }
        }

        return new ServerConfig
        {
            Mode = mode,
            Port = port,
            BindAddress = string.IsNullOrWhiteSpace(bind) ? "127.0.0.1" : bind.Trim(),
            FfmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? null : ffmpegPath,
            DataDirectory = string.IsNullOrWhiteSpace(dataDir) ? null : dataDir,
            GetSongBpmKey = string.IsNullOrWhiteSpace(getSongBpmKey) ? null : getSongBpmKey,
            AcoustIdKey = string.IsNullOrWhiteSpace(acoustIdKey) ? null : acoustIdKey,
            FpcalcPath = string.IsNullOrWhiteSpace(fpcalcPath) ? null : fpcalcPath,
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value.");
        return args[++i];
    }

    private static int RequireInt(string[] args, ref int i, string flag)
    {
        string raw = RequireValue(args, ref i, flag);
        if (!int.TryParse(raw, out int value) || value is < 1 or > 65535)
            throw new ArgumentException($"{flag} must be a port number 1–65535, got '{raw}'.");
        return value;
    }
}
