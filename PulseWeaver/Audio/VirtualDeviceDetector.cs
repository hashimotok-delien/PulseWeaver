namespace PulseWeaver;

internal sealed record VirtualAudioDevice(
    string PlaybackName,
    AudioDeviceOption Playback,
    AudioDeviceOption? Recording)
{
    public string RecordingName => Recording?.Name ?? "（対応する録音デバイスが見つかりません）";

    public string SuggestText => Recording is null
        ? "この出力を選ぶと、受け側のアプリは同じ名前の録音デバイスを選んでください。"
        : $"受け側のアプリ（Discord など）のマイクに「{Recording.Name}」を選びます。";
}

/// <summary>
/// VB-CABLE / VoiceMeeter / SYNCROOM などの仮想オーディオデバイスを見つけて、
/// 再生側と録音側のペアを推定する。
/// </summary>
internal static class VirtualDeviceDetector
{
    private static readonly string[] Keywords =
    {
        "CABLE Input",
        "CABLE Output",
        "VB-Audio",
        "VoiceMeeter",
        "SYNCROOM",
        "Virtual Audio Cable",
        "BlackHole",
        "VAC"
    };

    public static bool IsVirtual(string friendlyName) =>
        Keywords.Any(keyword => friendlyName.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<VirtualAudioDevice> Detect(
        IEnumerable<AudioDeviceOption> playbackDevices,
        IEnumerable<AudioDeviceOption> recordingDevices)
    {
        var recordings = recordingDevices.Where(device => device.Id.Length > 0).ToArray();
        var result = new List<VirtualAudioDevice>();

        foreach (var playback in playbackDevices)
        {
            if (playback.Id.Length == 0 || !IsVirtual(playback.Name))
            {
                continue;
            }

            result.Add(new VirtualAudioDevice(playback.Name, playback, FindRecording(playback.Name, recordings)));
        }

        return result;
    }

    private static AudioDeviceOption? FindRecording(string playbackName, AudioDeviceOption[] recordings)
    {
        var expected = playbackName
            .Replace("CABLE Input", "CABLE Output", StringComparison.OrdinalIgnoreCase)
            .Replace("Input", "Output", StringComparison.OrdinalIgnoreCase);

        return recordings.FirstOrDefault(device => string.Equals(device.Name, expected, StringComparison.OrdinalIgnoreCase))
            ?? recordings.FirstOrDefault(device => string.Equals(device.Name, playbackName, StringComparison.OrdinalIgnoreCase));
    }
}
