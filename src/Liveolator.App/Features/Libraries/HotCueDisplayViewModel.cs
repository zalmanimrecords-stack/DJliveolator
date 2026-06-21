using System;
using System.Reactive;
using System.Reactive.Linq;
using Liveolator.Core.Analysis.Cues;
using ReactiveUI;

namespace Liveolator.App.Features.Libraries;

/// <summary>
/// Display projection of one stored <see cref="HotCue"/> for the Libraries detail panel: the 1-based pad
/// number, its position formatted as m:ss.cc, an optional performer label, the optional 0xRRGGBB pad color,
/// and whether the cue is an unconfirmed auto-placed suggestion. Two performer actions are exposed:
/// <see cref="ConfirmCommand"/> commits a suggestion (so re-analysis preserves it), and
/// <see cref="DeleteCommand"/> removes the cue. Presentation only — the cue data lives in the cue store;
/// the actions are delegated to the owning view-model, which persists the change.
/// </summary>
public sealed class HotCueDisplayViewModel
{
    private const string None = "—";

    /// <param name="cue">The stored cue to project.</param>
    /// <param name="sampleRate">Sample rate the cue offset is measured against (for the time display).</param>
    /// <param name="onConfirm">Commit-a-suggestion callback (cue index); null disables Confirm.</param>
    /// <param name="onDelete">Delete-the-cue callback (cue index); null disables Delete.</param>
    public HotCueDisplayViewModel(
        HotCue cue, int sampleRate, Action<int>? onConfirm = null, Action<int>? onDelete = null)
    {
        Index = cue.Index;
        Number = (cue.Index + 1).ToString(); // pads read 1..N in the UI (same as the deck's hot-cue pads)
        Time = FormatTime(cue, sampleRate);
        Label = string.IsNullOrWhiteSpace(cue.Label) ? None : cue.Label!;
        Color = cue.Color;
        IsAuto = cue.IsAuto;

        // Confirm only makes sense for an unconfirmed suggestion; Delete applies to any stored cue.
        CanConfirm = IsAuto && onConfirm is not null;
        CanDelete = onDelete is not null;
        ConfirmCommand = ReactiveCommand.Create(() => onConfirm?.Invoke(Index), Observable.Return(CanConfirm));
        DeleteCommand = ReactiveCommand.Create(() => onDelete?.Invoke(Index), Observable.Return(CanDelete));
    }

    /// <summary>Zero-based hot-cue slot index (drives the confirm/delete callbacks).</summary>
    public int Index { get; }

    /// <summary>1-based pad number.</summary>
    public string Number { get; }

    /// <summary>Cue position from track start, formatted m:ss.cc (or "—" for an unknown sample rate).</summary>
    public string Time { get; }

    /// <summary>Performer label, or "—" when the cue is unlabelled.</summary>
    public string Label { get; }

    /// <summary>Optional 0xRRGGBB pad color, or null when unset (drives the swatch color).</summary>
    public int? Color { get; }

    /// <summary>True when this cue was auto-placed and not yet confirmed by the DJ (a "suggested" cue).</summary>
    public bool IsAuto { get; }

    /// <summary>Tag shown after the label: "auto" for suggested cues, empty for committed/manual cues.</summary>
    public string Tag => IsAuto ? "auto" : string.Empty;

    /// <summary>True when the Confirm action is available (the cue is a suggestion and a handler was wired).</summary>
    public bool CanConfirm { get; }

    /// <summary>True when the Delete action is available (a handler was wired).</summary>
    public bool CanDelete { get; }

    /// <summary>Commits this suggestion to a manual cue so re-analysis preserves it (suggested → commit).</summary>
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    /// <summary>Removes this cue from the track's stored cue set.</summary>
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    private static string FormatTime(HotCue cue, int sampleRate)
    {
        if (sampleRate <= 0)
            return None;

        var t = TimeSpan.FromSeconds(cue.PositionSeconds(sampleRate));
        // m:ss.cc — sub-second precision matters for cue placement, unlike the whole-track duration.
        return $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }
}
