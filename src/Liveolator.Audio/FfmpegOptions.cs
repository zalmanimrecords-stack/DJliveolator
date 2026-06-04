namespace Liveolator.Audio;

/// <summary>
/// Configures how <see cref="FfmpegAudioDecoder"/> locates the FFmpeg executable for the
/// CLI-subprocess decode path. Resolution: an explicit path, then the
/// <c>LIVEOLATOR_FFMPEG_PATH</c> environment variable, otherwise the bare command
/// <c>ffmpeg</c> (resolved via PATH).
/// </summary>
public sealed class FfmpegOptions
{
    public const string EnvironmentVariable = "LIVEOLATOR_FFMPEG_PATH";

    /// <summary>Path to (or bare name of) the FFmpeg executable.</summary>
    public string ExecutablePath { get; }

    public FfmpegOptions(string? executablePath = null)
        => ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? "ffmpeg" : executablePath.Trim();

    /// <summary>Builds options from the <c>LIVEOLATOR_FFMPEG_PATH</c> environment variable.</summary>
    public static FfmpegOptions FromEnvironment()
        => new(Environment.GetEnvironmentVariable(EnvironmentVariable));
}
