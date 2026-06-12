namespace Liveolator.App.Features.Settings;

/// <summary>
/// One selectable output channel-pair in the Settings audio pickers (doc 12): its 0-based pair
/// <see cref="Index"/> (persisted into <c>AudioSettings.MasterOutputPair</c>/<c>CueOutputPair</c>) and
/// the human-readable <see cref="Label"/> the ComboBox shows (e.g. "Outputs 3/4"). Built from the
/// selected device's channel count via <c>Liveolator.Core.Audio.OutputChannelPair</c>, so the label and
/// the routing index always agree. A record so ComboBox selection matching is by value.
/// </summary>
public sealed record OutputPairOption(int Index, string Label);
