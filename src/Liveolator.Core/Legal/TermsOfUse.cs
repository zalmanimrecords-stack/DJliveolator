namespace Liveolator.Core.Legal;

/// <summary>
/// The application's Terms of Use / liability disclaimer — a single source of truth shared by the
/// first-launch acceptance dialog and the read-only Settings view, so the text the performer accepts is
/// always the same text they can re-read later. Pure data (no UI, no IO) so it unit-tests in Core.
/// </summary>
/// <remarks>
/// <see cref="CurrentVersion"/> is bumped whenever <see cref="Text"/> changes materially; a higher
/// version than the one a user previously accepted re-triggers the acceptance gate (see
/// <c>LegalSettings.HasAcceptedCurrentTerms</c>). This is plain boilerplate, not legal advice — have it
/// reviewed by a qualified lawyer before relying on it.
/// </remarks>
public static class TermsOfUse
{
    /// <summary>The version of the terms text below. Bump on any material change to <see cref="Text"/>.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Heading shown above the terms in both the dialog and the Settings view.</summary>
    public const string Title = "Terms of Use and Disclaimer of Liability";

    /// <summary>
    /// The full terms text. Kept ASCII-only and free of UI markup so it renders identically in the
    /// acceptance dialog and the Settings view, and so it is safe to persist/transport.
    /// </summary>
    public const string Text =
        "Please read these terms carefully before using Liveolator. By installing or using this " +
        "software you agree to the terms below. If you do not agree, do not use the software.\n" +
        "\n" +
        "1. No warranty. Liveolator is provided \"AS IS\" and \"AS AVAILABLE\", without warranty of any " +
        "kind, whether express, implied, or statutory. This includes, without limitation, any implied " +
        "warranties of merchantability, fitness for a particular purpose, title, and non-infringement. " +
        "The author does not warrant that the software will be uninterrupted, error-free, or free of " +
        "harmful components, or that it will meet your requirements.\n" +
        "\n" +
        "2. Disclaimer of liability. To the maximum extent permitted by applicable law, the author of " +
        "Liveolator shall not be liable for any direct, indirect, incidental, special, consequential, " +
        "exemplary, or punitive damages of any kind whatsoever. This includes, without limitation, " +
        "damage to or loss of audio or visual equipment, hearing damage, loss of data, loss of profits " +
        "or revenue, business interruption, or any damages arising out of a live performance, however " +
        "caused and under any theory of liability, even if the author has been advised of the " +
        "possibility of such damages.\n" +
        "\n" +
        "3. Your responsibility. You use Liveolator entirely at your own risk. You are solely " +
        "responsible for your hardware, audio levels, content, and the conduct and outcome of any " +
        "performance. You are responsible for keeping backups of your data and for verifying that the " +
        "software is suitable for your intended use before relying on it in a live or professional " +
        "setting.\n" +
        "\n" +
        "4. Third-party content. Liveolator may process media files, devices, and third-party " +
        "components that it does not own. You are responsible for holding all rights and licenses " +
        "required for any content you load, play, or display.\n" +
        "\n" +
        "5. Limitation. Some jurisdictions do not allow the exclusion of certain warranties or the " +
        "limitation of certain liabilities, so some of the above limitations may not apply to you. In " +
        "that case, the author's liability is limited to the smallest extent permitted by law.\n" +
        "\n" +
        "By choosing to accept, you confirm that you have read, understood, and agree to these terms.";

    /// <summary>The terms with a version heading appended, for display in the dialog and Settings view.</summary>
    public static string DisplayText => $"{Title} (v{CurrentVersion})\n\n{Text}";
}
