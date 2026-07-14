namespace Liveolator.App.Features.Update;

/// <summary>The action the user picked in the "update available" dialog.</summary>
public enum UpdateDialogChoice
{
    /// <summary>Dismiss for now; offer again on the next launch.</summary>
    Later,

    /// <summary>Open the installer download link in the browser.</summary>
    Download,

    /// <summary>Don't offer this specific version again (persisted as the skipped version).</summary>
    Skip,
}
