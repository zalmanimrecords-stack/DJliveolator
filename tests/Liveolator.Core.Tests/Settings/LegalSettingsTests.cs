using Liveolator.Core.Legal;
using Liveolator.Core.Settings;
using Xunit;

namespace Liveolator.Core.Tests.Settings;

public sealed class LegalSettingsTests
{
    [Fact]
    public void Default_HasNotAcceptedAnyTerms()
    {
        Assert.Equal(0, LegalSettings.Default.AcceptedTermsVersion);
        Assert.False(LegalSettings.Default.HasAcceptedCurrentTerms);
    }

    [Fact]
    public void AcceptedCurrent_RecordsTheCurrentTermsVersion()
    {
        Assert.Equal(TermsOfUse.CurrentVersion, LegalSettings.AcceptedCurrent.AcceptedTermsVersion);
        Assert.True(LegalSettings.AcceptedCurrent.HasAcceptedCurrentTerms);
    }

    [Fact]
    public void HasAcceptedCurrentTerms_IsFalse_WhenAcceptedVersionIsOlder()
    {
        var accepted = new LegalSettings(TermsOfUse.CurrentVersion - 1);

        Assert.False(accepted.HasAcceptedCurrentTerms);
    }

    [Fact]
    public void HasAcceptedCurrentTerms_IsTrue_WhenAcceptedVersionIsNewer()
    {
        // A user who accepted a future terms version (e.g. after a downgrade) is not re-prompted.
        var accepted = new LegalSettings(TermsOfUse.CurrentVersion + 1);

        Assert.True(accepted.HasAcceptedCurrentTerms);
    }

    [Fact]
    public void Normalized_FoldsNegativeAcceptedVersionToZero()
    {
        var corrupt = new LegalSettings(AcceptedTermsVersion: -5);

        Assert.Equal(0, corrupt.Normalized().AcceptedTermsVersion);
        Assert.False(corrupt.Normalized().HasAcceptedCurrentTerms);
    }

    [Fact]
    public void Normalized_LeavesValidVersionUntouched()
    {
        var settings = new LegalSettings(TermsOfUse.CurrentVersion);

        Assert.Equal(settings, settings.Normalized());
    }
}
