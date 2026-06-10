using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Liveolator.App.Shell;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using ReactiveUI;

namespace Liveolator.App.Features.Addons;

/// <summary>
/// Settings for the built-in VU-meter add-on. The VU meter is a single self-contained generator that
/// samples its dial face as the background and draws the needle over it, so the performer can (a) choose
/// where the needle pivots — <b>Bottom</b> (classic, needle up) or <b>Top</b> (needle hangs down) — and
/// (b) replace the dial face with a custom image. Both changes <b>persist</b> (<see cref="ISettingsStore"/>)
/// and apply <b>live</b> through <see cref="_applyLive"/> (the composition root re-registers the generator
/// and refreshes the composition — never a direct engine call from the VM). The page documents the
/// required size + needle pivot for the current origin from <see cref="VuMeterFaceSpec"/>. UI-free.
/// </summary>
public sealed class VuMeterBackgroundSettingsViewModel : ViewModelBase
{
    // Aspect tolerance before warning the face will be stretched out of shape (±2% of the target ratio).
    private const double AspectTolerance = 0.02;

    private readonly ISettingsStore _store;
    private readonly Func<VuMeterNeedleOrigin, VuMeterFaceSpec> _specFor;
    private readonly Func<VuMeterNeedleOrigin, string> _defaultFaceFor;
    private readonly Action<string?, VuMeterNeedleOrigin>? _applyLive;
    private readonly IImageDimensionsProbe? _imageProbe;

    private string? _customPath;
    private VuMeterNeedleOrigin _origin;
    private string _imagePath;
    private string? _aspectWarning;
    private string _status = string.Empty;

    public VuMeterBackgroundSettingsViewModel(
        ISettingsStore store,
        Func<VuMeterNeedleOrigin, VuMeterFaceSpec> specFor,
        Func<VuMeterNeedleOrigin, string> defaultFaceFor,
        string? currentCustomPath = null,
        VuMeterNeedleOrigin origin = VuMeterNeedleOrigin.Bottom,
        Action<string?, VuMeterNeedleOrigin>? applyLive = null,
        IImageDimensionsProbe? imageProbe = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _specFor = specFor ?? throw new ArgumentNullException(nameof(specFor));
        _defaultFaceFor = defaultFaceFor ?? throw new ArgumentNullException(nameof(defaultFaceFor));
        _applyLive = applyLive;
        _imageProbe = imageProbe;

        _origin = origin;
        _customPath = string.IsNullOrWhiteSpace(currentCustomPath) ? null : currentCustomPath!.Trim();
        _imagePath = _customPath ?? _defaultFaceFor(origin);

        ResetToDefaultCommand = ReactiveCommand.CreateFromTask(ResetToDefaultAsync);
        if (_customPath is not null)
            RecomputeAspectWarning(_customPath);
    }

    /// <summary>The authoring spec (size + pivot + needle sweep) for the current origin.</summary>
    public VuMeterFaceSpec Spec => _specFor(_origin);

    /// <summary>The active face image path — the built-in face for the origin, or the chosen custom one.</summary>
    public string ImagePath
    {
        get => _imagePath;
        private set => this.RaiseAndSetIfChanged(ref _imagePath, value);
    }

    /// <summary>True when a custom face is in use (enables "Reset to default").</summary>
    public bool IsCustom => _customPath is not null;

    /// <summary>The needle-origin options for the selector.</summary>
    public IReadOnlyList<VuMeterNeedleOrigin> Origins { get; } =
        new[] { VuMeterNeedleOrigin.Bottom, VuMeterNeedleOrigin.Top };

    /// <summary>The chosen needle origin; setting it persists + applies live + refreshes the guidance.</summary>
    public VuMeterNeedleOrigin SelectedOrigin
    {
        get => _origin;
        set
        {
            if (value == _origin)
                return;
            _origin = value;
            this.RaisePropertyChanged();
            // Built-in face follows the origin; a custom face is kept as-is.
            ImagePath = _customPath ?? _defaultFaceFor(_origin);
            RaiseGuidanceChanged();
            RecomputeAspectWarning(_customPath);
            _ = ApplyAsync(_customPath, _origin, "Needle origin updated.");
        }
    }

    /// <summary>Non-null when the chosen image's aspect differs from the recommended one (advice only).</summary>
    public string? AspectWarning
    {
        get => _aspectWarning;
        private set => this.RaiseAndSetIfChanged(ref _aspectWarning, value);
    }

    /// <summary>Last action outcome, shown to the performer.</summary>
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public ReactiveCommand<Unit, Unit> ResetToDefaultCommand { get; }

    // ── Documentation strings (single source of truth: VuMeterFaceSpec) ─────────────────────────────

    private bool NeedleFromTop => _origin == VuMeterNeedleOrigin.Top;

    public string SizeRequirement =>
        string.Format(
            CultureInfo.CurrentCulture,
            "Recommended image size: {0} x {1} px ({2} aspect). Other sizes are accepted and stretched to "
            + "fill the meter - keep the {2} aspect so the dial stays in shape.",
            Spec.RecommendedWidth, Spec.RecommendedHeight, AspectLabel);

    /// <summary>A ready-to-paste prompt for an AI image generator that produces a matching dial face for
    /// the current needle origin. The app draws the needle, so the prompt forbids one and pins the pivot.</summary>
    public string ImagePrompt =>
        string.Join(Environment.NewLine, new[]
        {
            string.Format(
                CultureInfo.CurrentCulture,
                "Design a photorealistic ANALOG VU-METER DIAL FACE as a background image, {0}x{1} px ({2}).",
                Spec.RecommendedWidth, Spec.RecommendedHeight, AspectLabel),
            "",
            "IMPORTANT - this is the BACKGROUND ONLY. Do NOT draw the pointer/needle: the app overlays the "
            + "moving needle itself. Leave the hub and the needle's path clear of any drawn pointer.",
            "",
            NeedleFromTop
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    "NEEDLE PIVOT & DIRECTION (critical): the needle pivots from the TOP and hangs DOWN. "
                    + "Draw a small round hub at the TOP-CENTRE - horizontal centre, {0:P0} down from the "
                    + "top edge, exactly at pixel ({1}, {2}) in a {3}x{4} image. The needle hangs down from "
                    + "this hub and swings like a pendulum: down-and-LEFT when quiet, down-and-RIGHT when loud.",
                    Spec.PivotYFraction, Spec.PivotXPixels, Spec.PivotYPixels,
                    Spec.RecommendedWidth, Spec.RecommendedHeight)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    "NEEDLE PIVOT & DIRECTION (critical): classic VU layout - the needle pivots from the "
                    + "BOTTOM and points UP. Draw a small round hub at the BOTTOM-CENTRE - horizontal centre, "
                    + "{0:P0} down from the top edge, exactly at pixel ({1}, {2}) in a {3}x{4} image. The "
                    + "needle rises from this hub: up-and-LEFT when quiet, up-and-RIGHT when loud.",
                    Spec.PivotYFraction, Spec.PivotXPixels, Spec.PivotYPixels,
                    Spec.RecommendedWidth, Spec.RecommendedHeight),
            "",
            string.Format(
                CultureInfo.CurrentCulture,
                "SCALE: print the curved numbered scale on the side the needle sweeps ({0} the hub), "
                + "following the needle tip's arc of radius about {1:P0} of the image height (~{2} px) "
                + "centred on the hub. Low/quiet marks on the LEFT, loud marks plus a RED zone on the RIGHT. "
                + "The needle travels about {3:0} to {4:0} degrees from centre, so place the scale ends just "
                + "past those extremes.",
                NeedleFromTop ? "below" : "above",
                Spec.ArcRadiusFraction, Spec.ArcRadiusPixels, Spec.NeedleMinDegrees, Spec.NeedleMaxDegrees),
            "",
            "STYLE: aged cream dial, dark bezel, subtle wear, a 'VU' legend in the open area. Fill the whole "
            + "frame. No needle, no extra text, no watermark.",
        });

    // The recommended aspect as a tidy "3:2"-style label derived from the spec's pixel size.
    private string AspectLabel
    {
        get
        {
            int g = Gcd(Spec.RecommendedWidth, Spec.RecommendedHeight);
            return g > 0
                ? $"{Spec.RecommendedWidth / g}:{Spec.RecommendedHeight / g}"
                : $"{Spec.AspectRatio:0.00}:1";
        }
    }

    /// <summary>
    /// Chooses a custom face image (called by the view after the file picker): validate it exists, persist
    /// it, apply it live for the current origin, and refresh the aspect advisory. A blank/missing path is
    /// reported and ignored rather than persisting an unusable value.
    /// </summary>
    public async Task ChooseImageAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Status = "No image selected.";
            return;
        }

        string trimmed = path.Trim();
        if (!File.Exists(trimmed))
        {
            Status = $"Image not found: {trimmed}";
            return;
        }

        _customPath = trimmed;
        this.RaisePropertyChanged(nameof(IsCustom));
        ImagePath = trimmed;
        RecomputeAspectWarning(trimmed);
        await ApplyAsync(trimmed, _origin, "Background image applied.").ConfigureAwait(false);
    }

    private async Task ResetToDefaultAsync()
    {
        _customPath = null;
        this.RaisePropertyChanged(nameof(IsCustom));
        ImagePath = _defaultFaceFor(_origin);
        AspectWarning = null;
        await ApplyAsync(null, _origin, "Restored the built-in VU-meter face.").ConfigureAwait(false);
    }

    // Persist the add-on settings (preserving the other section), apply live, and report. Returns quietly
    // on a persistence failure (reported via Status) rather than throwing into the UI.
    private async Task ApplyAsync(string? customPath, VuMeterNeedleOrigin origin, string okStatus)
    {
        try
        {
            AppSettings current = await _store.LoadAsync().ConfigureAwait(false);
            AppSettings updated = current with
            {
                Addons = current.Addons with
                {
                    VuMeterBackgroundImagePath = customPath,
                    VuMeterNeedleOrigin = origin,
                },
            };
            await _store.SaveAsync(updated).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save the setting: {ex.Message}";
            return;
        }

        _applyLive?.Invoke(customPath, origin);
        Status = okStatus;
    }

    private void RaiseGuidanceChanged()
    {
        this.RaisePropertyChanged(nameof(Spec));
        this.RaisePropertyChanged(nameof(SizeRequirement));
        this.RaisePropertyChanged(nameof(ImagePrompt));
    }

    private void RecomputeAspectWarning(string? path)
    {
        if (path is null || _imageProbe is null
            || !_imageProbe.TryGetPixelSize(path, out int w, out int h) || h <= 0 || w <= 0)
        {
            AspectWarning = null;
            return;
        }

        double aspect = (double)w / h;
        double target = Spec.AspectRatio;
        if (target > 0 && Math.Abs(aspect - target) > AspectTolerance * target)
        {
            AspectWarning = string.Format(
                CultureInfo.CurrentCulture,
                "This image is {0} x {1} ({2:0.00}:1). The meter expects {3:0.00}:1 ({4} x {5}); it will be "
                + "stretched, so the needle may not line up with the dial.",
                w, h, aspect, target, Spec.RecommendedWidth, Spec.RecommendedHeight);
        }
        else
        {
            AspectWarning = null;
        }
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
            (a, b) = (b, a % b);
        return Math.Abs(a);
    }
}
