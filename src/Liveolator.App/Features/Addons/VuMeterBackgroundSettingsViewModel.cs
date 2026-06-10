using System;
using System.Globalization;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Liveolator.App.Shell;
using Liveolator.Core.Actions;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Visuals;
using Liveolator.Visuals.Gl;
using ReactiveUI;

namespace Liveolator.App.Features.Addons;

/// <summary>
/// Settings for the built-in VU-meter add-on: replace the static dial-face (background) image while the
/// needle stays standard. Picking an image <b>persists</b> it (<see cref="ISettingsStore"/>) and applies
/// it <b>live</b> by dispatching a <see cref="PerformanceActionKind.VisualSetLayerSource"/> at the face
/// layer's slot (doc 04 — the UI is just another action source; never a direct engine call). The page
/// documents the required size + needle pivot from <see cref="VuMeterFaceSpec"/> so a custom face lines
/// up with the needle. UI-free and unit-testable with fakes.
/// </summary>
public sealed class VuMeterBackgroundSettingsViewModel : ViewModelBase
{
    // Aspect tolerance before warning the face will be stretched out of shape (±2% of the target ratio).
    private const double AspectTolerance = 0.02;

    private readonly IPerformanceActionDispatcher _dispatcher;
    private readonly ISettingsStore _store;
    private readonly IImageDimensionsProbe? _imageProbe;
    private readonly int? _faceLayerSlot;
    private readonly string _defaultFacePath;

    private string _imagePath;
    private bool _isCustom;
    private string? _aspectWarning;
    private string _status = string.Empty;

    public VuMeterBackgroundSettingsViewModel(
        IPerformanceActionDispatcher dispatcher,
        ISettingsStore store,
        int? faceLayerSlot,
        string defaultFacePath,
        VuMeterFaceSpec spec,
        string? currentCustomPath = null,
        IImageDimensionsProbe? imageProbe = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _faceLayerSlot = faceLayerSlot;
        _defaultFacePath = defaultFacePath ?? throw new ArgumentNullException(nameof(defaultFacePath));
        _imageProbe = imageProbe;

        _isCustom = !string.IsNullOrWhiteSpace(currentCustomPath);
        _imagePath = _isCustom ? currentCustomPath!.Trim() : _defaultFacePath;

        ResetToDefaultCommand = ReactiveCommand.CreateFromTask(ResetToDefaultAsync);
        if (_isCustom)
            RecomputeAspectWarning(_imagePath);
    }

    /// <summary>The authoring spec (size + pivot + needle sweep) shown to the performer.</summary>
    public VuMeterFaceSpec Spec { get; }

    /// <summary>The active face image path — the built-in face or the chosen custom one.</summary>
    public string ImagePath
    {
        get => _imagePath;
        private set => this.RaiseAndSetIfChanged(ref _imagePath, value);
    }

    /// <summary>True when a custom face is in use (enables "Reset to default").</summary>
    public bool IsCustom
    {
        get => _isCustom;
        private set => this.RaiseAndSetIfChanged(ref _isCustom, value);
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

    public string SizeRequirement =>
        string.Format(
            CultureInfo.CurrentCulture,
            "Recommended image size: {0} × {1} px ({2} aspect). Other sizes are accepted and stretched to "
            + "fill the meter — keep the {2} aspect so the dial stays in shape.",
            Spec.RecommendedWidth, Spec.RecommendedHeight, AspectLabel);

    /// <summary>
    /// A ready-to-paste prompt for an AI image generator that produces a matching background (dial face).
    /// The app draws the moving needle on top, so the prompt forbids a needle and pins the exact pivot the
    /// app's needle hangs from — built from <see cref="VuMeterFaceSpec"/> so the numbers can't drift.
    /// </summary>
    public string ImagePrompt =>
        string.Join(Environment.NewLine, new[]
        {
            string.Format(
                CultureInfo.CurrentCulture,
                "Design a photorealistic ANALOG VU-METER DIAL FACE as a background image, {0}×{1} px ({2}).",
                Spec.RecommendedWidth, Spec.RecommendedHeight, AspectLabel),
            "",
            "IMPORTANT: this is the BACKGROUND only. Do NOT draw the pointer/needle — the app renders the "
            + "moving needle on top. Leave the pivot and the area the needle sweeps clean and unobstructed.",
            "",
            string.Format(
                CultureInfo.CurrentCulture,
                "The needle pivots at the TOP and hangs DOWN over the scale. Put the pivot hub at exactly "
                + "horizontal centre, {0:P0} down from the top — pixel ({1}, {2}) in a {3}×{4} image — and "
                + "draw a small brass/metal hub there.",
                Spec.PivotYFraction, Spec.PivotXPixels, Spec.PivotYPixels,
                Spec.RecommendedWidth, Spec.RecommendedHeight),
            "",
            string.Format(
                CultureInfo.CurrentCulture,
                "Paint a curved scale BELOW the hub: an upward 'smile' arc of radius ≈{0:P0} of the image "
                + "height (≈{1} px) centred on the hub, with tick marks, dB numbers and a red zone toward "
                + "the right end. The needle will sweep this arc from about {2:0}° (far left) to {3:0}° (far "
                + "right) measured from straight down, so align the scale to that range.",
                Spec.ArcRadiusFraction, Spec.ArcRadiusPixels, Spec.NeedleMinDegrees, Spec.NeedleMaxDegrees),
            "",
            "Style: aged cream dial, dark bezel, subtle wear, a 'VU' legend near the bottom centre. Fill the "
            + "whole frame. No needle, no extra text, no watermark.",
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
    /// Chooses a custom face image (called by the view after the file picker): validate it exists,
    /// persist the path, apply it live, and refresh the aspect advisory. A blank/missing path is
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

        if (!await PersistAsync(trimmed).ConfigureAwait(false))
            return;

        ApplyFace(trimmed);
        ImagePath = trimmed;
        IsCustom = true;
        RecomputeAspectWarning(trimmed);
        Status = "Background image applied.";
    }

    private async Task ResetToDefaultAsync()
    {
        if (!await PersistAsync(null).ConfigureAwait(false))
            return;

        ApplyFace(_defaultFacePath);
        ImagePath = _defaultFacePath;
        IsCustom = false;
        AspectWarning = null;
        Status = "Restored the built-in VU-meter face.";
    }

    // Read-modify-write only the add-on section so other settings are preserved (last-writer-wins is
    // acceptable for this rarely-touched tab). Returns false (and reports) when persistence fails.
    private async Task<bool> PersistAsync(string? customPath)
    {
        try
        {
            AppSettings current = await _store.LoadAsync().ConfigureAwait(false);
            AppSettings updated = current with { Addons = new AddonSettings(customPath) };
            await _store.SaveAsync(updated).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save the setting: {ex.Message}";
            return false;
        }
    }

    // Apply live via the action dispatcher (the seam) — swap the face layer's image source immediately.
    // With no face layer in the running scene (e.g. headless/tests), there is nothing to apply live;
    // the persisted choice still takes effect on the next launch.
    private void ApplyFace(string path)
    {
        if (_faceLayerSlot is not { } slot)
            return;

        string encoded = VisualSourceActionCodec.Encode(new VisualSourceRef(VisualSourceKind.Image, path));
        _dispatcher.Dispatch(new PerformanceAction(
            PerformanceActionKind.VisualSetLayerSource,
            Slot: slot,
            Argument: encoded));
    }

    private void RecomputeAspectWarning(string path)
    {
        if (_imageProbe is null || !_imageProbe.TryGetPixelSize(path, out int w, out int h) || h <= 0 || w <= 0)
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
                "This image is {0} × {1} ({2:0.00}:1). The meter expects {3:0.00}:1 ({4} × {5}); it will "
                + "be stretched, so the needle may not line up with the dial.",
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
