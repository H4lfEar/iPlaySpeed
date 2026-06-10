using System.Runtime.InteropServices;
using System.Text;

namespace IPlaySpeed.App.Native;

/// <summary>Win32 P/Invoke 선언 모음.</summary>
internal static class NativeMethods
{
    // ── 포그라운드 창 추적 ──
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // ── 프로세스 실행파일 경로 (권한 높은/보호된 프로세스도 조회 가능) ──
    // Process.MainModule.FileName 은 PROCESS_QUERY_INFORMATION + 모듈 열거가 필요해
    // 관리자 권한으로 도는 게임(안티치트 등)에서 실패한다. QueryFullProcessImageName 은
    // PROCESS_QUERY_LIMITED_INFORMATION 만으로 무결성 수준이 달라도 경로를 읽을 수 있다.
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    /// <summary>PID로 실행파일 전체 경로를 조회. 실패하면 null.</summary>
    internal static string? GetProcessImagePath(uint pid)
    {
        if (pid == 0) return null;
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        catch { return null; }
        finally { CloseHandle(h); }
    }

    // ── 전역 단축키 ──
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const int WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    // ── 아이콘 추출 (SHGetFileInfo) ──
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_LARGEICON = 0x000000000;
    internal const uint SHGFI_SMALLICON = 0x000000001;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    internal static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);
}
