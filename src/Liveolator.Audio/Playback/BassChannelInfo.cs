namespace Liveolator.Audio.Playback;

/// <summary>Channel format reported by BASS.</summary>
/// <remarks>
/// Lived in <c>IBassPlayback.cs</c> until that seam — the single-deck playback path — was removed. It
/// outlived its old home because the capture backend and the mixer backend describe their channels
/// with it too.
/// </remarks>
internal readonly record struct BassChannelInfo(int Channels, int SampleRate);
