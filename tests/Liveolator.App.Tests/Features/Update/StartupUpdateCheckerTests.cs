using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Liveolator.App.Features.Update;
using Liveolator.Core.Persistence;
using Liveolator.Core.Settings;
using Liveolator.Core.Update;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Liveolator.App.Tests.Features.Update;

/// <summary>
/// Verifies the startup update coordinator: it honours the enabled flag, only prompts on a genuinely
/// newer build, opens the download link on "Download", persists the skipped version on "Skip", and never
/// throws when a leg of the flow fails.
/// </summary>
public sealed class StartupUpdateCheckerTests
{
    private static UpdateManifest Manifest(string version)
        => new(version, $"https://example.test/Setup-{version}.exe", new List<string> { "note" });

    private sealed class FakeManifestSource : IUpdateManifestSource
    {
        private readonly UpdateManifest? _manifest;
        private readonly bool _throw;
        public FakeManifestSource(UpdateManifest? manifest, bool @throw = false) { _manifest = manifest; _throw = @throw; }
        public Task<UpdateManifest?> FetchAsync(CancellationToken ct = default)
            => _throw ? throw new InvalidOperationException("boom") : Task.FromResult(_manifest);
    }

    private sealed class FakeVersionProvider : IInstalledVersionProvider
    {
        public FakeVersionProvider(string version) => CurrentVersion = version;
        public string CurrentVersion { get; }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings ToLoad { get; set; } = AppSettings.Default;
        public AppSettings? Saved { get; private set; }
        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(ToLoad);
        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            Saved = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePrompt : IUpdatePrompt
    {
        private readonly UpdateDialogChoice _choice;
        public FakePrompt(UpdateDialogChoice choice) => _choice = choice;
        public int Calls { get; private set; }
        public Task<UpdateDialogChoice> PromptAsync(UpdateManifest manifest, string currentVersion)
        {
            Calls++;
            return Task.FromResult(_choice);
        }
    }

    private sealed class FakeUrlOpener : IUrlOpener
    {
        public string? Opened { get; private set; }
        public void Open(string url) => Opened = url;
    }

    private static StartupUpdateChecker Build(
        IUpdateManifestSource source,
        IInstalledVersionProvider version,
        ISettingsStore store,
        IUpdatePrompt prompt,
        IUrlOpener urlOpener)
        => new(source, version, store, prompt, urlOpener, NullLogger<StartupUpdateChecker>.Instance);

    [Fact]
    public async Task WhenDisabled_DoesNotFetchOrPrompt()
    {
        var prompt = new FakePrompt(UpdateDialogChoice.Later);
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with { Updates = new UpdateSettings(CheckOnStartup: false) },
        };
        // A source that would throw if touched proves the disabled flag short-circuits before the fetch.
        var checker = Build(new FakeManifestSource(null, @throw: true), new FakeVersionProvider("0.1.4"),
            store, prompt, new FakeUrlOpener());

        await checker.CheckAsync();

        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task WhenUpToDate_DoesNotPrompt()
    {
        var prompt = new FakePrompt(UpdateDialogChoice.Later);
        var checker = Build(new FakeManifestSource(Manifest("0.1.4")), new FakeVersionProvider("0.1.4"),
            new FakeSettingsStore(), prompt, new FakeUrlOpener());

        await checker.CheckAsync();

        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task WhenNewer_PromptsAndDownloadOpensTheLink()
    {
        var prompt = new FakePrompt(UpdateDialogChoice.Download);
        var opener = new FakeUrlOpener();
        var checker = Build(new FakeManifestSource(Manifest("0.1.5")), new FakeVersionProvider("0.1.4"),
            new FakeSettingsStore(), prompt, opener);

        await checker.CheckAsync();

        Assert.Equal(1, prompt.Calls);
        Assert.Equal("https://example.test/Setup-0.1.5.exe", opener.Opened);
    }

    [Fact]
    public async Task Skip_PersistsTheSkippedVersion()
    {
        var prompt = new FakePrompt(UpdateDialogChoice.Skip);
        var store = new FakeSettingsStore();
        var checker = Build(new FakeManifestSource(Manifest("0.1.5")), new FakeVersionProvider("0.1.4"),
            store, prompt, new FakeUrlOpener());

        await checker.CheckAsync();

        Assert.Equal("0.1.5", store.Saved!.Updates.SkippedVersion);
    }

    [Fact]
    public async Task Skip_DoesNotDisableFutureChecks()
    {
        var store = new FakeSettingsStore();
        var checker = Build(new FakeManifestSource(Manifest("0.1.5")), new FakeVersionProvider("0.1.4"),
            store, new FakePrompt(UpdateDialogChoice.Skip), new FakeUrlOpener());

        await checker.CheckAsync();

        Assert.True(store.Saved!.Updates.CheckOnStartup);
    }

    [Fact]
    public async Task Later_NeitherOpensNorPersists()
    {
        var opener = new FakeUrlOpener();
        var store = new FakeSettingsStore();
        var checker = Build(new FakeManifestSource(Manifest("0.1.5")), new FakeVersionProvider("0.1.4"),
            store, new FakePrompt(UpdateDialogChoice.Later), opener);

        await checker.CheckAsync();

        Assert.Null(opener.Opened);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task AlreadySkippedVersion_DoesNotPrompt()
    {
        var prompt = new FakePrompt(UpdateDialogChoice.Later);
        var store = new FakeSettingsStore
        {
            ToLoad = AppSettings.Default with { Updates = new UpdateSettings(SkippedVersion: "0.1.5") },
        };
        var checker = Build(new FakeManifestSource(Manifest("0.1.5")), new FakeVersionProvider("0.1.4"),
            store, prompt, new FakeUrlOpener());

        await checker.CheckAsync();

        Assert.Equal(0, prompt.Calls);
    }

    [Fact]
    public async Task FetchFailure_IsSwallowed_AndDoesNotPrompt()
    {
        var prompt = new FakePrompt(UpdateDialogChoice.Later);
        var checker = Build(new FakeManifestSource(null, @throw: true), new FakeVersionProvider("0.1.4"),
            new FakeSettingsStore(), prompt, new FakeUrlOpener());

        await checker.CheckAsync(); // must not throw

        Assert.Equal(0, prompt.Calls);
    }
}
