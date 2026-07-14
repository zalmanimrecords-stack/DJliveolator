using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Concurrency;
using System.Reactive.Threading.Tasks;
using Liveolator.App.Features.Mappings;
using Liveolator.Core.Actions;
using Liveolator.Core.Mapping;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Media;
using ReactiveUI;
using Xunit;

namespace Liveolator.App.Tests.Mappings;

/// <summary>
/// Export/import of a MIDI mapping by device model (doc 05): Export writes the connected device's profile
/// to the picked file; Import installs a profile file under the connected device's name so Settings -> Save
/// applies it. No device / no profile is reported, not crashed.
/// </summary>
public sealed class MappingsViewModelExportImportTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "liveolator-vm-midimap-tests", Guid.NewGuid().ToString("N"));

    public MappingsViewModelExportImportTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static ControllerMappingProfile Sample(string name = "CMD STUDIO 2A") => new(
        name, "CMD Studio 2A",
        new[] { new ControllerBinding(MidiMessageType.ControlChange, 0, 1, PerformanceActionKind.MixerCrossfade, ActionInputMode.Absolute) });

    [Fact]
    public async Task Export_writes_the_active_profile_to_the_picked_path()
    {
        var session = new FakeSession { ActiveProfile = Sample(), InputDeviceName = "CMD STUDIO 2A" };
        var portability = new FakePortability();
        var picker = new FakeFilePicker { ExportPath = Path.Combine(_root, "out.json") };
        var vm = NewVm(session, portability, picker);

        await vm.ExportMappingCommand.Execute().ToTask();

        Assert.Same(session.ActiveProfile, portability.Exported);
        Assert.Equal(picker.ExportPath, portability.ExportedPath);
        Assert.Contains("Exported", vm.Status);
        Assert.Contains("CMD", picker.LastSuggested); // suggested filename derives from the device model
    }

    [Fact]
    public async Task Export_with_no_active_profile_reports_and_writes_nothing()
    {
        var session = new FakeSession { ActiveProfile = null };
        var portability = new FakePortability();
        var vm = NewVm(session, portability, new FakeFilePicker { ExportPath = Path.Combine(_root, "out.json") });

        await vm.ExportMappingCommand.Execute().ToTask();

        Assert.Null(portability.Exported);
        Assert.Contains("No mapping", vm.Status);
    }

    [Fact]
    public async Task Import_installs_the_profile_under_the_connected_device_name()
    {
        var store = new LiveProfileStore(_root);
        var session = new FakeSession { InputDeviceName = "My CMD Box" };
        var portability = new FakePortability { ImportResult = Sample("Exported Model") };
        var picker = new FakeFilePicker { ImportPath = Path.Combine(_root, "in.json") };
        var vm = NewVm(session, portability, picker, store);

        await vm.ImportMappingCommand.Execute().ToTask();

        // Re-keyed to the connected device so Save (which loads by device name) applies it.
        ControllerMappingProfile? installed = await store.LoadMappingProfileAsync("My CMD Box");
        Assert.NotNull(installed);
        Assert.Single(installed!.Bindings);
        Assert.Equal("My CMD Box", installed.Name);
        Assert.Contains("Save to apply", vm.Status);
    }

    [Fact]
    public async Task Import_with_no_device_reports_and_does_not_install()
    {
        var store = new LiveProfileStore(_root);
        var session = new FakeSession { InputDeviceName = null };
        var portability = new FakePortability { ImportResult = Sample() };
        var vm = NewVm(session, portability, new FakeFilePicker { ImportPath = Path.Combine(_root, "in.json") }, store);

        await vm.ImportMappingCommand.Execute().ToTask();

        Assert.Contains("Connect", vm.Status);
    }

    private static MappingsViewModel NewVm(
        FakeSession session, FakePortability portability, FakeFilePicker picker, ILiveProfileStore? store = null)
        => new(session, presets: null, profileStore: store, portability: portability, filePicker: picker);

    private sealed class FakeFilePicker : IMappingFilePicker
    {
        public string? ExportPath { get; set; }
        public string? ImportPath { get; set; }
        public string? LastSuggested { get; private set; }
        public Task<string?> PickExportPathAsync(string suggestedFileName)
        {
            LastSuggested = suggestedFileName;
            return Task.FromResult(ExportPath);
        }
        public Task<string?> PickImportPathAsync() => Task.FromResult(ImportPath);
    }

    private sealed class FakePortability : IMappingProfilePortability
    {
        public ControllerMappingProfile? Exported { get; private set; }
        public string? ExportedPath { get; private set; }
        public ControllerMappingProfile? ImportResult { get; set; }
        public Task ExportAsync(ControllerMappingProfile profile, string filePath, CancellationToken cancellationToken = default)
        {
            Exported = profile;
            ExportedPath = filePath;
            return Task.CompletedTask;
        }
        public Task<ControllerMappingProfile?> ImportAsync(string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult(ImportResult);
    }

    private sealed class FakeSession : IMidiControlSession
    {
        public ControllerMappingProfile? ActiveProfile { get; set; }
        public string? InputDeviceName { get; set; }
        public bool IsLearnArmed => false;
        public bool IsInputConnected => InputDeviceName is not null;
        public bool IsOutputConnected => false;
        public string? OutputDeviceName => null;
        public event EventHandler? ActivityDetected { add { } remove { } }
        public event EventHandler<ControllerMappingProfile>? MappingChanged { add { } remove { } }
        public Task StartAsync(MidiSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop() { }
        public void BeginLearn(
            PerformanceActionKind action, int slot = 0, string? argument = null,
            ActionInputMode? preferredInputMode = null, double relativeTicksPerRevolution = 1.0, bool invert = false,
            RelativeEncoding relativeEncoding = RelativeEncoding.TwosComplement) { }
        public void CancelLearn() { }
        public Task RemoveBindingAsync(ControllerBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
