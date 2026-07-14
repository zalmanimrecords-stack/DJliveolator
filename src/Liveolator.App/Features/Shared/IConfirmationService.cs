namespace Liveolator.App.Features.Shared;

/// <summary>
/// Shows a modal yes/no confirmation and resolves to the user's choice. Abstracted so view-models can
/// gate a destructive action (e.g. deleting a file) on confirmation without depending on Avalonia,
/// keeping them unit-testable. The Avalonia implementation is <see cref="ConfirmationService"/>.
/// </summary>
public interface IConfirmationService
{
    /// <summary>Returns <c>true</c> if the user confirms, <c>false</c> if they cancel or dismiss it.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "OK");
}
