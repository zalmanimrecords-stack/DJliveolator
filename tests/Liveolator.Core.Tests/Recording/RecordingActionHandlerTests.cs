using Liveolator.Core.Actions;
using Liveolator.Core.Recording;
using Xunit;

namespace Liveolator.Core.Tests.Recording;

public sealed class RecordingActionHandlerTests
{
    [Fact]
    public void Handle_FirstToggle_StartsRecording_AtProvidedPath()
    {
        var recorder = new FakeMasterRecorder();
        var paths = new FixedRecordingPathProvider("C:/recordings/set-001.wav");
        var handler = new RecordingActionHandler(recorder, paths);

        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle));

        Assert.True(recorder.IsRecording);
        Assert.Equal("C:/recordings/set-001.wav", recorder.LastStartPath);
    }

    [Fact]
    public void Handle_SecondToggle_StopsRecording()
    {
        var recorder = new FakeMasterRecorder();
        var handler = new RecordingActionHandler(recorder, new FixedRecordingPathProvider("p.wav"));

        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle)); // start
        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle)); // stop

        Assert.False(recorder.IsRecording);
        Assert.Equal(1, recorder.StopCount);
    }

    [Fact]
    public void Handle_ExplicitArgumentPath_OverridesProvider()
    {
        var recorder = new FakeMasterRecorder();
        var handler = new RecordingActionHandler(recorder, new FixedRecordingPathProvider("provider.wav"));

        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle, Argument: "explicit.wav"));

        Assert.Equal("explicit.wav", recorder.LastStartPath);
    }

    [Fact]
    public void Handle_RaisesActiveFeedback_OnStart_AndInactive_OnStop()
    {
        var recorder = new FakeMasterRecorder();
        var handler = new RecordingActionHandler(recorder, new FixedRecordingPathProvider("p.wav"));
        var states = new List<ActionFeedbackState>();
        handler.FeedbackChanged += (_, e) =>
        {
            Assert.Equal(PerformanceActionKind.MasterRecordToggle, e.Kind);
            states.Add(e.State);
        };

        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle)); // start
        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle)); // stop

        Assert.Equal(2, states.Count);
        Assert.True(states[0].IsActive);
        Assert.True(states[0].IsAvailable);
        Assert.False(states[1].IsActive);
    }

    [Fact]
    public void GetFeedback_ReflectsRecorderState()
    {
        var recorder = new FakeMasterRecorder();
        var handler = new RecordingActionHandler(recorder, new FixedRecordingPathProvider("p.wav"));

        ActionFeedbackState before = handler.GetFeedback(PerformanceActionKind.MasterRecordToggle, 0);
        Assert.False(before.IsActive);
        Assert.True(before.IsAvailable);

        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle));

        Assert.True(handler.GetFeedback(PerformanceActionKind.MasterRecordToggle, 0).IsActive);
    }

    [Fact]
    public void GetFeedback_Unavailable_WhenRecorderUnavailable()
    {
        var recorder = new FakeMasterRecorder { IsAvailable = false };
        var handler = new RecordingActionHandler(recorder, new FixedRecordingPathProvider("p.wav"));

        ActionFeedbackState feedback = handler.GetFeedback(PerformanceActionKind.MasterRecordToggle, 0);

        Assert.False(feedback.IsAvailable);
    }

    [Fact]
    public void Handle_Unavailable_DoesNotStart_ReportsUnavailable()
    {
        var recorder = new FakeMasterRecorder { IsAvailable = false };
        var handler = new RecordingActionHandler(recorder, new FixedRecordingPathProvider("p.wav"));
        ActionFeedbackState? raised = null;
        handler.FeedbackChanged += (_, e) => raised = e.State;

        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle));

        Assert.False(recorder.IsRecording);
        Assert.NotNull(raised);
        Assert.False(raised!.IsAvailable);
    }

    [Fact]
    public void Handle_StartReturnsFalse_DoesNotLatchOn()
    {
        var recorder = new FakeMasterRecorder { StartSucceeds = false };
        var handler = new RecordingActionHandler(recorder, new FixedRecordingPathProvider("p.wav"));

        handler.Handle(new PerformanceAction(PerformanceActionKind.MasterRecordToggle));

        // Start failed; a second toggle must try to start again (not call Stop), so the latch follows
        // the recorder's truth, never a stale local flag.
        Assert.False(handler.GetFeedback(PerformanceActionKind.MasterRecordToggle, 0).IsActive);
        Assert.Equal(0, recorder.StopCount);
    }

    [Fact]
    public void HandledKinds_OwnsOnlyMasterRecordToggle()
    {
        var handler = new RecordingActionHandler(new FakeMasterRecorder(), new FixedRecordingPathProvider("p.wav"));

        Assert.Equal(new[] { PerformanceActionKind.MasterRecordToggle }, handler.HandledKinds);
    }

    private sealed class FakeMasterRecorder : IMasterRecorder
    {
        public bool IsAvailable { get; set; } = true;
        public bool StartSucceeds { get; set; } = true;
        public bool IsRecording { get; private set; }
        public string? LastStartPath { get; private set; }
        public int StopCount { get; private set; }

        public bool Start(string path)
        {
            if (!IsAvailable || IsRecording || !StartSucceeds)
                return false;
            LastStartPath = path;
            IsRecording = true;
            return true;
        }

        public void Stop()
        {
            if (!IsRecording)
                return;
            IsRecording = false;
            StopCount++;
        }
    }

    private sealed class FixedRecordingPathProvider : IRecordingPathProvider
    {
        private readonly string _path;
        public FixedRecordingPathProvider(string path) => _path = path;
        public string NextRecordingPath() => _path;
    }
}
