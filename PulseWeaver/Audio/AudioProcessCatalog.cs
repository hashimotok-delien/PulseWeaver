using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace PulseWeaver;

internal sealed record AudioProcessCandidate(int ProcessId, string ProcessName, string Title, bool IsPlayingAudio)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? ProcessName : $"{ProcessName} — {Title}";

    public string Detail => IsPlayingAudio ? $"PID {ProcessId} / 再生中" : $"PID {ProcessId}";
}

/// <summary>
/// アプリ単位の取り込みで選べる候補（音声セッションのあるアプリ＋ウィンドウのあるアプリ）を集める。
/// </summary>
internal static class AudioProcessCatalog
{
    public static IReadOnlyList<AudioProcessCandidate> GetCandidates()
    {
        var found = new Dictionary<int, AudioProcessCandidate>();

        CollectAudioSessions(found);
        CollectWindowedProcesses(found);

        return found.Values
            .OrderByDescending(candidate => candidate.IsPlayingAudio)
            .ThenBy(candidate => candidate.ProcessName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static int? FindProcessIdByName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        try
        {
            var processes = Process.GetProcessesByName(processName)
                .Where(process => process.Id != Environment.ProcessId)
                .OrderByDescending(process => process.StartTime)
                .ToArray();

            try
            {
                return processes.Length == 0 ? null : processes[0].Id;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Error($"プロセス「{processName}」を検索できませんでした。", exception);
            return null;
        }
    }

    private static void CollectAudioSessions(Dictionary<int, AudioProcessCandidate> found)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var session = sessions[i];
                            var processId = (int)session.GetProcessID;
                            if (processId <= 0 || processId == Environment.ProcessId)
                            {
                                continue;
                            }

                            var candidate = Describe(processId);
                            if (candidate is null)
                            {
                                continue;
                            }

                            var playing = session.State == AudioSessionState.AudioSessionStateActive;
                            found[processId] = candidate with
                            {
                                IsPlayingAudio = playing || (found.TryGetValue(processId, out var existing) && existing.IsPlayingAudio)
                            };
                        }
                        catch (Exception)
                        {
                            // セッションによっては取得できないものがある
                        }
                    }
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("音声セッションの列挙に失敗しました。", exception);
        }
    }

    private static void CollectWindowedProcesses(Dictionary<int, AudioProcessCandidate> found)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId || found.ContainsKey(process.Id))
                {
                    continue;
                }

                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                var candidate = Describe(process.Id);
                if (candidate is not null)
                {
                    found[process.Id] = candidate;
                }
            }
            catch (Exception)
            {
                // アクセスできないプロセスは無視する
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static AudioProcessCandidate? Describe(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var title = process.MainWindowTitle;
            return new AudioProcessCandidate(processId, process.ProcessName, title, false);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
