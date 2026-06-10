using System.Diagnostics;
using System.IO;

namespace IPlaySpeed.App.Services;

/// <summary>
/// Windows 로그온 시 자동 실행 등록/해제. 이 앱은 관리자 권한으로 동작해야 하므로,
/// 시작프로그램(HKCU Run)으로 등록하면 부팅마다 UAC가 떠서 불편하다. 그래서 대신
/// '최고 권한으로 실행' 작업 스케줄러 작업을 만들어 로그온 시 UAC 없이 승격 실행되게 한다.
/// (작업 생성에는 관리자 권한이 필요 — 앱이 승격 상태일 때 동작)
/// </summary>
public static class StartupManager
{
    private const string TaskName = "iPlaySpeed Startup";

    /// <summary>시작 프로그램으로 등록되어 있는지.</summary>
    public static bool IsEnabled()
    {
        try { return RunSchtasks("/Query", "/TN", TaskName) == 0; }
        catch { return false; }
    }

    /// <summary>등록/해제. 성공하면 true.</summary>
    public static bool SetEnabled(bool enable)
    {
        try
        {
            if (enable)
            {
                string exe = StartupTargetPath();
                int code = RunSchtasks(
                    "/Create", "/TN", TaskName,
                    "/TR", $"\"{exe}\"",
                    "/SC", "ONLOGON",
                    "/RL", "HIGHEST",
                    "/F");
                return code == 0;
            }
            else
            {
                RunSchtasks("/Delete", "/TN", TaskName, "/F");
                return true;
            }
        }
        catch { return false; }
    }

    /// <summary>로그온 시 실행할 exe 경로. Velopack 설치형이면 버전이 올라가도 유지되는 상위 stub exe 사용.</summary>
    private static string StartupTargetPath()
    {
        string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;
        try
        {
            string? dir = Path.GetDirectoryName(exe);
            if (dir is not null && string.Equals(Path.GetFileName(dir), "current", StringComparison.OrdinalIgnoreCase))
            {
                string stub = Path.GetFullPath(Path.Combine(dir, "..", Path.GetFileName(exe)));
                if (File.Exists(stub)) return stub;
            }
        }
        catch { /* 무시 */ }
        return exe;
    }

    private static int RunSchtasks(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(8000);
        return p.HasExited ? p.ExitCode : -1;
    }
}
