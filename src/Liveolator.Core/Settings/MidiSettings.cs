namespace Liveolator.Core.Settings;

/// <summary>
/// User-chosen MIDI controller settings (doc 05/12): which attached input device drives the
/// performance (Push 1 / CMD STUDIO 2A) and which output device receives LED/feedback. Stored as
/// device <b>names</b> (matched case-insensitively, like <c>RtMidiDeviceProvider</c>), so a selection
/// survives re-plugging and differing platform device indices. Pure data the App persists.
/// </summary>
/// <remarks>
/// A null name means "none selected" (control still works without a feedback output — doc 06).
/// <see cref="Normalized"/> folds blank/whitespace selections to null so an empty picker value is
/// never persisted as a real selection.
/// </remarks>
public sealed record MidiSettings
{
    /// <summary>Name (or substring) of the controller input device, or null for none selected.</summary>
    public string? ControllerInputName { get; init; }

    /// <summary>Name (or substring) of the feedback/LED output device, or null for none selected.</summary>
    public string? FeedbackOutputName { get; init; }

    /// <summary>No controller selected.</summary>
    public static MidiSettings Default { get; } = new();

    /// <summary>Returns a copy with blank/whitespace device selections folded to null.</summary>
    public MidiSettings Normalized()
        => this with
        {
            ControllerInputName = Blank(ControllerInputName),
            FeedbackOutputName = Blank(FeedbackOutputName),
        };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
