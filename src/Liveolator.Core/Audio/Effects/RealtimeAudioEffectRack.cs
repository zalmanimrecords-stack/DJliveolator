namespace Liveolator.Core.Audio.Effects;

/// <summary>
/// Realtime-safe effect rack. Control operations replace an immutable processing snapshot; the
/// audio callback only reads that snapshot and invokes processors, with no locks or allocations.
/// </summary>
public sealed class RealtimeAudioEffectRack : IAudioEffectRack, IDisposable
{
    private sealed class Entry
    {
        public required string InstanceId { get; init; }
        public required string PluginUid { get; init; }
        public required IAudioEffectProcessor? Processor { get; init; }
        public bool IsBypassed { get; set; }
        public Dictionary<string, double> Parameters { get; } = new(StringComparer.Ordinal);
        public byte[]? OpaqueState { get; set; }
    }

    private readonly int _slot;
    private readonly IAudioEffectProcessorFactory _factory;
    private readonly object _gate = new();
    private Entry[] _entries = Array.Empty<Entry>();

    public RealtimeAudioEffectRack(int slot, IAudioEffectProcessorFactory factory)
    {
        if (slot < 0 || slot >= AudioEffectRackSlot.Count)
            throw new ArgumentOutOfRangeException(nameof(slot));
        _slot = slot;
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public AudioEffectRackState State
    {
        get
        {
            Entry[] entries = Volatile.Read(ref _entries);
            return new AudioEffectRackState(
                _slot,
                entries.Select(ToState).ToArray(),
                entries.Where(e => !e.IsBypassed).Sum(e => e.Processor?.LatencySamples ?? 0));
        }
    }

    public string Load(string pluginUid, string? instanceId = null)
    {
        if (string.IsNullOrWhiteSpace(pluginUid))
            throw new ArgumentException("Plugin UID is required.", nameof(pluginUid));

        string id = string.IsNullOrWhiteSpace(instanceId) ? Guid.NewGuid().ToString("N") : instanceId;
        lock (_gate)
        {
            Entry[] current = _entries;
            if (current.Any(e => string.Equals(e.InstanceId, id, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Effect instance '{id}' is already loaded.");

            _factory.TryCreate(pluginUid, out IAudioEffectProcessor? processor);
            var entry = new Entry { InstanceId = id, PluginUid = pluginUid, Processor = processor };
            Publish(current.Append(entry));
        }
        return id;
    }

    public void Unload(string instanceId)
    {
        lock (_gate)
        {
            Entry entry = Find(instanceId);
            Publish(_entries.Where(e => !ReferenceEquals(e, entry)));
            entry.Processor?.Dispose();
        }
    }

    public void Move(string instanceId, int index)
    {
        lock (_gate)
        {
            var list = _entries.ToList();
            Entry entry = Find(instanceId);
            list.Remove(entry);
            list.Insert(Math.Clamp(index, 0, list.Count), entry);
            Publish(list);
        }
    }

    public void ToggleBypass(string instanceId)
    {
        lock (_gate)
        {
            Entry entry = Find(instanceId);
            entry.IsBypassed = !entry.IsBypassed;
            Publish(_entries);
        }
    }

    public void SetParameter(string instanceId, string parameterId, double normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(parameterId))
            throw new ArgumentException("Parameter id is required.", nameof(parameterId));
        double value = Math.Clamp(normalizedValue, 0, 1);
        lock (_gate)
        {
            Entry entry = Find(instanceId);
            entry.Processor?.SetParameter(parameterId, value);
            entry.Parameters[parameterId] = value;
            Publish(_entries);
        }
    }

    public void LoadPreset(string instanceId, ReadOnlySpan<byte> state)
    {
        byte[] copy = state.ToArray();
        lock (_gate)
        {
            Entry entry = Find(instanceId);
            entry.Processor?.LoadPreset(copy);
            entry.OpaqueState = copy;
            Publish(_entries);
        }
    }

    public void Process(Span<float> interleaved, int channels)
    {
        Entry[] entries = Volatile.Read(ref _entries);
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (!entry.IsBypassed)
                entry.Processor?.Process(interleaved, channels);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (Entry entry in _entries)
                entry.Processor?.Dispose();
            Publish(Array.Empty<Entry>());
        }
    }

    private Entry Find(string instanceId)
        => _entries.FirstOrDefault(e => string.Equals(e.InstanceId, instanceId, StringComparison.Ordinal))
           ?? throw new KeyNotFoundException($"Effect instance '{instanceId}' is not loaded.");

    private void Publish(IEnumerable<Entry> entries) => Volatile.Write(ref _entries, entries.ToArray());

    private static AudioEffectInstanceState ToState(Entry entry)
        => new(
            entry.InstanceId,
            entry.PluginUid,
            entry.IsBypassed,
            new Dictionary<string, double>(entry.Parameters, StringComparer.Ordinal),
            entry.OpaqueState?.ToArray(),
            IsMissing: entry.Processor is null);
}

public sealed class AudioEffectRackProvider : IAudioEffectRackProvider, IDisposable
{
    private readonly RealtimeAudioEffectRack[] _racks;

    public AudioEffectRackProvider(IAudioEffectProcessorFactory factory)
        => _racks = Enumerable.Range(0, AudioEffectRackSlot.Count)
            .Select(slot => new RealtimeAudioEffectRack(slot, factory))
            .ToArray();

    public IAudioEffectRack GetRack(int slot)
        => slot >= 0 && slot < _racks.Length
            ? _racks[slot]
            : throw new ArgumentOutOfRangeException(nameof(slot));

    public void Dispose()
    {
        foreach (RealtimeAudioEffectRack rack in _racks)
            rack.Dispose();
    }
}
