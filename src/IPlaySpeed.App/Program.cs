using System;
using System.Diagnostics;
using System.Security.Principal;
using Velopack;

namespace IPlaySpeed.App;

/// <summary>
/// 명시적 진입점.
/// 1) Velopack 훅을 가장 먼저 실행한다(설치/업데이트/제거 시 Update.exe가 특수 인자로 이 앱을 실행 →
///    훅 처리 후 종료). 이때는 asInvoker 그대로 동작해야 하므로 권한 상승을 하지 않는다.
/// 2) 일반 실행이면 관리자 권한이 아닐 때 스스로 관리자로 재실행한다(안티치트 게임 관리에 필요).
///    → 설치/업데이트는 권한 상승 없이, 실제 사용 시에만 UAC 1회.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 1) Velopack 훅(설치/업데이트/제거). 훅이면 내부에서 처리 후 프로세스 종료된다.
        VelopackApp.Build().Run();

        // 2) 비승격 단계: 이미 실행 중이면 기존 창을 깨우고 종료(권한 상승/새 인스턴스 없이).
        if (!IsElevated())
        {
            if (AppInstance.AlreadyRunning())
            {
                AppInstance.SignalExisting();
                return;
            }
            // 첫 인스턴스 → 관리자로 승격(게임 관리에 필요)
            if (TryRelaunchAsAdmin(args))
                return;
            // 승격 실패(사용자 거부 등) → 비승격으로 계속
        }

        // 3) 주 인스턴스 등록. 경합으로 이미 있으면 기존을 깨우고 종료.
        if (!AppInstance.TryBecomePrimary())
        {
            AppInstance.SignalExisting();
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static bool TryRelaunchAsAdmin(string[] args)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true, // runas는 ShellExecute 필요
                Verb = "runas",
                Arguments = args.Length > 0 ? string.Join(' ', args) : string.Empty
            };
            Process.Start(psi);
            return true; // 승격 인스턴스 시작 → 현재(비승격) 종료
        }
        catch
        {
            // 사용자가 UAC를 거부한 경우 등 → 비승격으로 계속 실행(일부 기능 제한).
            return false;
        }
    }
}
