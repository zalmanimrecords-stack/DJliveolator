using Liveolator.Core.Analysis.Bpm;
using Liveolator.Core.Enrichment;
using Liveolator.Core.Library;
using Xunit;

namespace Liveolator.Core.Tests.Enrichment;

/// <summary>
/// The merge policy is the heart of online enrichment (doc 16): offline-first — the locally detected
/// value stays authoritative; an online value only cross-checks (agreement raises confidence to Ok)
/// or, when there is no local value, fills the gap (marked unverified). These rules are pure and fully
/// testable with no network.
/// </summary>
public class MetadataMergePolicyTests
{
    [Fact]
    public void Neither_IsUnknown_StatusUnchanged()
    {
        EnrichedBpm result = MetadataMergePolicy.MergeBpm(local: null, online: null, MediaAnalysisStatus.Failed);

        Assert.Null(result.Bpm);
        Assert.Equal(BpmProvenance.Unknown, result.Provenance);
        Assert.Equal(MediaAnalysisStatus.Failed, result.Status);
    }

    [Fact]
    public void LocalOnly_KeepsLocal_WithLocalProvenance()
    {
        var local = new BpmResult(140.0, 0.8);

        EnrichedBpm result = MetadataMergePolicy.MergeBpm(local, online: null, MediaAnalysisStatus.Ok);

        Assert.Equal(140.0, result.Bpm);
        Assert.Equal(0.8, result.Confidence, 6);
        Assert.Equal(BpmProvenance.LocalDetected, result.Provenance);
        Assert.Equal(MediaAnalysisStatus.Ok, result.Status);
    }

    [Fact]
    public void OnlineOnly_FillsValue_ButMarksUnverified()
    {
        // No local BPM (e.g. decode failed) — the online value is usable but not verified against the
        // actual file, so it is flagged PartiallyAnalyzed with a modest confidence.
        EnrichedBpm result = MetadataMergePolicy.MergeBpm(local: null, online: 140.0, MediaAnalysisStatus.Failed);

        Assert.Equal(140.0, result.Bpm);
        Assert.Equal(BpmProvenance.OnlineFetched, result.Provenance);
        Assert.Equal(MediaAnalysisStatus.PartiallyAnalyzed, result.Status);
        Assert.True(result.Confidence is > 0 and < 0.9);
    }

    [Fact]
    public void Agreement_CrossChecks_RaisesConfidence_StatusOk()
    {
        var local = new BpmResult(140.2, 0.55); // low local confidence...

        EnrichedBpm result = MetadataMergePolicy.MergeBpm(local, online: 140.0, MediaAnalysisStatus.PartiallyAnalyzed);

        Assert.Equal(140.2, result.Bpm);                       // keep the local (file-accurate) value
        Assert.Equal(BpmProvenance.CrossChecked, result.Provenance);
        Assert.Equal(MediaAnalysisStatus.Ok, result.Status);   // ...promoted by the agreement
        Assert.True(result.Confidence >= 0.9);
    }

    [Fact]
    public void Agreement_AcrossHalfDoubleTime_StillCrossChecks()
    {
        // Psytrance edge case: local detects 70, the catalogued tempo is 140 — same track, octave apart.
        var local = new BpmResult(70.0, 0.6);

        EnrichedBpm result = MetadataMergePolicy.MergeBpm(local, online: 140.0, MediaAnalysisStatus.PartiallyAnalyzed);

        Assert.Equal(BpmProvenance.CrossChecked, result.Provenance);
        Assert.Equal(MediaAnalysisStatus.Ok, result.Status);
    }

    [Fact]
    public void Disagreement_KeepsLocal_FlagsForReview()
    {
        var local = new BpmResult(128.0, 0.7);

        EnrichedBpm result = MetadataMergePolicy.MergeBpm(local, online: 174.0, MediaAnalysisStatus.Ok);

        Assert.Equal(128.0, result.Bpm);                          // offline-first: local stays authoritative
        Assert.Equal(BpmProvenance.Conflicted, result.Provenance);
        Assert.Equal(MediaAnalysisStatus.PartiallyAnalyzed, result.Status); // flagged for review
    }
}
