using System.Threading;
using IPlaySpeed.App.Native;

namespace IPlaySpeed.App;

/// <summary>
/// 단일 인스턴스 보장 + 기존 인스턴스 깨우기.
/// 앱이 관리자 권한(높은 무결성)으로 도는데, 작업표시줄 아이콘 재실행은 비승격(보통 무결성)으로
/// 시작되므로 경계를 넘는 신호가 필요하다. 감지는 뮤텍스(동기화=읽기, 무결성 위로 읽기 허용),
/// 깨우기는 전역 등록 메시지 브로드캐스트(수신 창이 ChangeWindowMessageFilterEx로 허용)로 처리한다.
/// </summary>
internal static class AppInstance
{
    private const string MutexName = @"Local\iPlaySpeed_SingleInstance_8e0f7a12";
    public static readonly uint ShowMessage =
        NativeMethods.RegisterWindowMessage("iPlaySpeed_Show_Window_8e0f7a12");

    private static Mutex? _mutex; // 주 인스턴스 동안 살아있어야 함(GC 방지)

    /// <summary>이미 다른 인스턴스가 실행 중인지.</summary>
    public static bool AlreadyRunning()
    {
        try
        {
            using var m = Mutex.OpenExisting(MutexName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; } // 없음
        catch { return true; } // 존재하나 접근 거부(승격 인스턴스를 비승격이 열 때) 등 → 존재로 간주
    }

    /// <summary>주 인스턴스로 등록. 이미 있으면 false.</summary>
    public static bool TryBecomePrimary()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        return createdNew;
    }

    /// <summary>실행 중인 인스턴스에게 '창을 보이라'고 신호.</summary>
    public static void SignalExisting() =>
        NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero);
}
