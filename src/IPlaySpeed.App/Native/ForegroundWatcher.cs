using System.Diagnostics;

namespace IPlaySpeed.App.Native;

/// <summary>현재 맨 앞(foreground) 창의 프로세스 정보를 알려준다.</summary>
public static class ForegroundWatcher
{
    /// <summary>
    /// 지금 foreground 창을 소유한 프로세스의 (이름 소문자, 실행파일 전체경로) 반환.
    /// 경로는 권한 문제로 못 읽으면 null. 알 수 없으면 ("", null).
    /// </summary>
    public static (string name, string? path) ForegroundProcessInfo()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return ("", null);
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
                return ("", null);
            using Process p = Process.GetProcessById((int)pid);
            string name = (p.ProcessName ?? "").ToLowerInvariant();
            // 관리자 권한 게임(안티치트)도 경로를 읽도록 QueryFullProcessImageName 우선 사용.
            string? path = NativeMethods.GetProcessImagePath(pid);
            if (path is null)
                try { path = p.MainModule?.FileName; } catch { /* 보호된 프로세스 등 */ }
            return (name, path);
        }
        catch
        {
            return ("", null);
        }
    }

    /// <summary>지금 foreground 창을 소유한 프로세스 이름(확장자 제외, 소문자). 알 수 없으면 빈 문자열.</summary>
    public static string ForegroundProcessName() => ForegroundProcessInfo().name;

    /// <summary>해당 프로세스명이 현재 실행 중인지.</summary>
    public static bool IsProcessRunning(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
