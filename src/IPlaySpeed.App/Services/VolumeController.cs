using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using IPlaySpeed.App.Native;
using IPlaySpeed.Core;

namespace IPlaySpeed.App.Services;

/// <summary>
/// CoreAudio(WASAPI) 세션 볼륨을 프로세스 단위로 조절한다.
/// 게임 오디오와 미디어 앱 오디오를 따로 줄일 수 있도록, 각 오디오 세션의 소유 PID를
/// 프로세스명/설치폴더로 매칭해 <see cref="SimpleAudioVolume"/>을 읽고 쓴다.
/// 모든 호출은 방어적으로 try/catch로 감싸 장치 부재/권한 문제에도 앱이 죽지 않게 한다.
/// </summary>
public sealed class VolumeController : IDisposable
{
    private readonly MMDeviceEnumerator? _enumerator;

    public VolumeController()
    {
        try { _enumerator = new MMDeviceEnumerator(); }
        catch { _enumerator = null; }
    }

    /// <summary>기본 재생 장치를 매번 새로 얻는다(기본 출력 장치 변경에 대응). 호출자가 Dispose.</summary>
    private MMDevice? GetDevice()
    {
        try { return _enumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch { return null; }
    }

    // ── 게임 ──

    /// <summary>현재 게임의 오디오 세션 평균 볼륨(0~1). 해당 세션이 없으면 null.</summary>
    public float? GetGameVolume(GameEntry entry) => GetVolume(s => SessionBelongsToGame(s, entry));

    /// <summary>현재 게임의 모든 오디오 세션 볼륨을 일괄 설정(0~1).</summary>
    public void SetGameVolume(GameEntry entry, float volume) => SetVolume(s => SessionBelongsToGame(s, entry), volume);

    // ── 미디어 ──

    /// <summary>미디어 출처 앱(프로세스명) 오디오 세션 평균 볼륨(0~1). 없으면 null.</summary>
    public float? GetMediaVolume(string processName) => GetVolume(s => SessionMatchesProcessName(s, processName));

    /// <summary>미디어 출처 앱(프로세스명) 오디오 세션 볼륨을 일괄 설정(0~1).</summary>
    public void SetMediaVolume(string processName, float volume) => SetVolume(s => SessionMatchesProcessName(s, processName), volume);

    // ── 내부 공통 ──

    private float? GetVolume(Func<AudioSessionControl, bool> match)
    {
        var dev = GetDevice();
        if (dev is null) return null;

        float sum = 0; int n = 0;
        try
        {
            var mgr = dev.AudioSessionManager;
            mgr.RefreshSessions();
            var sessions = mgr.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSessionControl s;
                try { s = sessions[i]; } catch { continue; }
                try { if (match(s)) { sum += s.SimpleAudioVolume.Volume; n++; } }
                catch { /* 만료된 세션 등 */ }
            }
        }
        catch { /* 장치/세션 접근 실패 */ }
        finally { try { dev.Dispose(); } catch { } }

        return n > 0 ? sum / n : (float?)null;
    }

    private void SetVolume(Func<AudioSessionControl, bool> match, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        var dev = GetDevice();
        if (dev is null) return;

        try
        {
            var mgr = dev.AudioSessionManager;
            mgr.RefreshSessions();
            var sessions = mgr.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSessionControl s;
                try { s = sessions[i]; } catch { continue; }
                try { if (match(s)) s.SimpleAudioVolume.Volume = volume; }
                catch { /* 만료된 세션 등 */ }
            }
        }
        catch { /* 장치/세션 접근 실패 */ }
        finally { try { dev.Dispose(); } catch { } }
    }

    /// <summary>
    /// 오디오 세션이 이 게임 소유인지: (1) 프로세스명 일치, 또는
    /// (2) 실행파일이 게임 설치 폴더 안에 있음. (런처로 켜 이름이 달라지는 게임 대응)
    /// </summary>
    private static bool SessionBelongsToGame(AudioSessionControl s, GameEntry entry)
    {
        var (name, path) = ProcInfo(s);
        if (name.Length == 0 && path is null) return false;

        string want = entry.EffectiveGameProcessName();
        if (!string.IsNullOrEmpty(want) && name.Equals(want, StringComparison.OrdinalIgnoreCase))
            return true;

        if (path is not null && !string.IsNullOrWhiteSpace(entry.GameFolder))
        {
            try
            {
                string folder = Path.GetFullPath(entry.GameFolder!)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { /* 경로 비교 실패 무시 */ }
        }
        return false;
    }

    private static bool SessionMatchesProcessName(AudioSessionControl s, string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var (name, _) = ProcInfo(s);
        return name.Length > 0 && name.Equals(processName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>오디오 세션 소유 프로세스의 (이름[확장자 제외], 실행파일 경로). 못 읽으면 ("", null).</summary>
    private static (string name, string? path) ProcInfo(AudioSessionControl s)
    {
        try
        {
            uint pid = s.GetProcessID;
            if (pid == 0) return ("", null);
            using var p = Process.GetProcessById((int)pid);
            string name = p.ProcessName ?? "";
            // 관리자 권한 게임도 경로를 읽도록 QueryFullProcessImageName 우선 사용.
            string? path = NativeMethods.GetProcessImagePath(pid);
            if (path is null)
                try { path = p.MainModule?.FileName; } catch { /* 권한/비트수 차이 */ }
            return (name, path);
        }
        catch { return ("", null); }
    }

    public void Dispose()
    {
        try { _enumerator?.Dispose(); } catch { }
    }
}
