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

internal static class MixerLimits
{
    /// <summary>音量スライダーの上限。1.0 = 0 dB、2.0 = 約 +6 dB。XAML 側の Slider Maximum と揃えること。</summary>
    public const double MaxVolume = 2.0;
}

internal sealed class MixerInputConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? Name { get; set; }

    public InputSourceKind Kind { get; set; }

    public string? DeviceId { get; set; }

    /// <summary>選択時のデバイス名。未接続のときの表示と、ID が変わったときの再照合に使う。</summary>
    public string? DeviceName { get; set; }

    public int? ProcessId { get; set; }

    public string? ProcessName { get; set; }

    public bool IncludeProcessTree { get; set; } = true;

    public double Volume { get; set; } = 0.8;

    public bool Muted { get; set; }

    public bool Solo { get; set; }

    /// <summary>モノラル化（左右をまとめる）。</summary>
    public bool Mono { get; set; }

    /// <summary>ノイズゲートの効き（0 = 切、10 = 最大）。</summary>
    public double Gate { get; set; }

    /// <summary>コンプレッサーの効き（0 = 切、10 = 最大）。</summary>
    public double Comp { get; set; }

    /// <summary>左右の定位（-1 = 左、0 = 中央、+1 = 右）。</summary>
    public double PanX { get; set; }

    /// <summary>前後の定位（-1 = 背面、0 = 中央、+1 = 正面）。</summary>
    public double PanY { get; set; }

    public string? MonitorOutputId { get; set; }

    public double MonitorVolume { get; set; } = 0.8;

    public Dictionary<string, bool> Routes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A1/B1 などストリップ表示名ごとの ON/OFF。出力 ID が振り直されても引き継げるよう保存する。</summary>
    public Dictionary<string, bool> RouteSlots { get; set; } = new(StringComparer.Ordinal);

    public bool GetRoute(string outputId) => Routes.TryGetValue(outputId, out var enabled) && enabled;

    public bool SetRoute(string outputId, bool enabled) => Routes[outputId] = enabled;

    public bool GetRouteSlot(string slot) => RouteSlots.TryGetValue(slot, out var enabled) && enabled;

    public void SetRouteSlot(string slot, bool enabled) => RouteSlots[slot] = enabled;

    [JsonIgnore]
    public bool HasAnyRoute => Routes.Values.Any(enabled => enabled);

    public MixerInputConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        DeviceId = DeviceId,
        DeviceName = DeviceName,
        ProcessId = ProcessId,
        ProcessName = ProcessName,
        IncludeProcessTree = IncludeProcessTree,
        Volume = Volume,
        Muted = Muted,
        Solo = Solo,
        Mono = Mono,
        Gate = Gate,
        Comp = Comp,
        PanX = PanX,
        PanY = PanY,
        MonitorOutputId = MonitorOutputId,
        MonitorVolume = MonitorVolume,
        Routes = new Dictionary<string, bool>(Routes, StringComparer.Ordinal),
        RouteSlots = new Dictionary<string, bool>(RouteSlots, StringComparer.Ordinal)
    };
}

internal sealed class MixerOutputConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? Name { get; set; }

    public OutputRole Role { get; set; }

    public string? DeviceId { get; set; }

    /// <summary>選択時のデバイス名。未接続のときの表示と、ID が変わったときの再照合に使う。</summary>
    public string? DeviceName { get; set; }

    public double Volume { get; set; } = 0.8;

    public bool Muted { get; set; }

    /// <summary>モノラル化（左右をまとめる）。</summary>
    public bool Mono { get; set; }

    public bool EqEnabled { get; set; }

    /// <summary>低域（120 Hz シェルフ）の増減 dB。</summary>
    public double EqLow { get; set; }

    /// <summary>中域（1 kHz ピーク）の増減 dB。</summary>
    public double EqMid { get; set; }

    /// <summary>高域（8 kHz シェルフ）の増減 dB。</summary>
    public double EqHigh { get; set; }

    public MixerOutputConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        Role = Role,
        DeviceId = DeviceId,
        DeviceName = DeviceName,
        Volume = Volume,
        Muted = Muted,
        Mono = Mono,
        EqEnabled = EqEnabled,
        EqLow = EqLow,
        EqMid = EqMid,
        EqHigh = EqHigh
    };
}
