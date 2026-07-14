using Liveolator.App.Composition;
using Liveolator.App.Tests.Live;
using Liveolator.Core.Actions;
using Liveolator.Core.Visuals;
using Liveolator.Media;
using Xunit;

namespace Liveolator.App.Tests.Composition;

public sealed class VisualSessionPersistenceTests
{
    private const string PsyFractal = "liveolator.builtin.psy-fractal/visualizer";

    [Fact]
    public async Task LayerSourceFeedback_SavesActiveSceneToLiveBank()
    {
        using var root = new TempRoot();
        var store = new LiveProfileStore(root.Path);
        var dispatcher = new FakeDispatcher();

        VisualScene scene = SceneWithLayer(0, new VisualSourceRef(VisualSourceKind.Generator, PsyFractal));
        using var persistence = new VisualSessionPersistence(dispatcher, () => scene, store);

        dispatcher.RaiseFeedback(
            PerformanceActionKind.VisualSetLayerSource,
            0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: PsyFractal));

        await persistence.WaitForPendingSaveAsync(TimeSpan.FromSeconds(2));

        VisualBank? saved = await store.LoadVisualBankAsync("Live");
        Assert.NotNull(saved);
        VisualScene savedScene = Assert.Single(saved!.Scenes);
        Assert.Equal(VisualSourceKind.Generator, savedScene.Layers[0].Source.Kind);
        Assert.Equal(PsyFractal, savedScene.Layers[0].Source.Reference);
    }

    [Fact]
    public async Task LiveBank_RoundTripsThroughTheStartupLoadPath()
    {
        using var root = new TempRoot();
        var store = new LiveProfileStore(root.Path);
        var dispatcher = new FakeDispatcher();

        VisualScene scene = SceneWithLayer(1, new VisualSourceRef(VisualSourceKind.Generator, PsyFractal));
        using (var persistence = new VisualSessionPersistence(dispatcher, () => scene, store))
        {
            dispatcher.RaiseFeedback(
                PerformanceActionKind.VisualSetLayerSource,
                1,
                new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0, Argument: PsyFractal));
            await persistence.WaitForPendingSaveAsync(TimeSpan.FromSeconds(2));
        }

        // The startup loader looks the bank up by the well-known "Live" name; it must be enumerable.
        IReadOnlyList<string> names = await store.ListVisualBankNamesAsync();
        Assert.Contains("Live", names);
    }

    [Fact]
    public async Task UnrelatedFeedback_DoesNotPersist()
    {
        using var root = new TempRoot();
        var store = new LiveProfileStore(root.Path);
        var dispatcher = new FakeDispatcher();

        VisualScene scene = SceneWithLayer(0, VisualSourceRef.None);
        using var persistence = new VisualSessionPersistence(dispatcher, () => scene, store);

        dispatcher.RaiseFeedback(
            PerformanceActionKind.VisualBlackout,
            0,
            new ActionFeedbackState(IsActive: true, IsAvailable: true, Value: 0));

        await persistence.WaitForPendingSaveAsync(TimeSpan.FromSeconds(1));

        Assert.Null(await store.LoadVisualBankAsync("Live"));
    }

    private static VisualScene SceneWithLayer(int slot, VisualSourceRef source)
    {
        var layers = new List<VisualLayer>();
        for (int i = 0; i <= slot; i++)
        {
            layers.Add(new VisualLayer(
                name: $"Layer {i + 1}",
                source: i == slot ? source : VisualSourceRef.None,
                effects: Array.Empty<EffectRef>(),
                blend: BlendMode.Normal,
                opacity: 1.0));
        }

        return new VisualScene(
            name: "Live",
            layers: layers,
            macroValues: new Dictionary<string, double>(),
            transition: TransitionStyle.Cut,
            beatBehavior: BeatBehavior.None);
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "liveolator-vis-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp dir; a locked file must not fail the test.
            }
        }
    }
}
