using System;
using System.Collections.Generic;
using Liveolator.Core.Audio;

namespace Liveolator.Audio.Tests.Playback;

/// <summary>Records every preload request so the test can assert what was warmed, and when.</summary>
internal sealed class FakeDeckPreloader : IDeckPreloader
{
    public List<string?> Requests { get; } = new();

    public string? ThrowOnPreloadOf { get; set; }

    public void Preload(string? trackPath)
    {
        Requests.Add(trackPath);
        if (trackPath is not null && trackPath == ThrowOnPreloadOf)
            throw new InvalidOperationException($"Simulated preload failure for '{trackPath}'.");
    }
}
