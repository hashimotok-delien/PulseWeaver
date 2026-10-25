using System.Text.Json.Serialization;

namespace PulseWeaver;

internal enum InputSourceKind
{
    Hardware,
    Loopback,
    Process
}

internal enum OutputRole
{
    Physical,
    Virtual
}

internal sealed class MixerInputConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? Name { get; set; }

    public InputSourceKind Kind { get; set; }

    public string? DeviceId { get; set; }

    public int? ProcessId { get; set; }

    public string? ProcessName { get; set; }

    public bool IncludeProcessTree { get; set; } = true;

    public double Volume { get; set; } = 0.8;

    public bool Muted { get; set; }

    public bool Solo { get; set; }

    public string? MonitorOutputId { get; set; }

    public double MonitorVolume { get; set; } = 0.8;

    public Dictionary<string, bool> Routes { get; set; } = new(StringComparer.Ordinal);

    public bool GetRoute(string outputId) => Routes.TryGetValue(outputId, out var enabled) && enabled;

    public bool SetRoute(string outputId, bool enabled) => Routes[outputId] = enabled;

    [JsonIgnore]
    public bool HasAnyRoute => Routes.Values.Any(enabled => enabled);

    public MixerInputConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        DeviceId = DeviceId,
        ProcessId = ProcessId,
        ProcessName = ProcessName,
        IncludeProcessTree = IncludeProcessTree,
        Volume = Volume,
        Muted = Muted,
        Solo = Solo,
        MonitorOutputId = MonitorOutputId,
        MonitorVolume = MonitorVolume,
        Routes = new Dictionary<string, bool>(Routes, StringComparer.Ordinal)
    };
}

internal sealed class MixerOutputConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? Name { get; set; }

    public OutputRole Role { get; set; }

    public string? DeviceId { get; set; }

    public double Volume { get; set; } = 0.8;

    public MixerOutputConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        Role = Role,
        DeviceId = DeviceId,
        Volume = Volume
    };
}
