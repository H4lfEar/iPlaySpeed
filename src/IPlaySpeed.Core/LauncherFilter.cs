namespace IPlaySpeed.Core;

/// <summary>
/// 게임 설치 폴더 안에서 도는 프로세스 중 '런처/업데이터/헬퍼'(실제 게임이 아님)를 가려낸다.
/// 타이머는 런처가 아니라 '실제 게임 exe'가 포그라운드/실행 중일 때만 작동해야 하므로 사용한다.
/// 순수 문자열 판별(확장자 제외 파일명, 대소문자 무시)이라 단위 테스트로 검증한다.
/// </summary>
public static class LauncherFilter
{
    // 실제 게임 프로세스가 아닐 가능성이 매우 높은 파일명 키워드(부분 일치).
    private static readonly string[] Keywords =
    {
        "launcher", "updater", "update", "hpatchz", "hyp", "crashreport", "crashpad",
        "helper", "unins", "setup", "redist", "vcredist", "dxsetup", "7z",
        "bootstrapper", "useless", "downloader", "installer", "patch",
    };

    /// <summary>해당 실행파일명(확장자 제외)이 런처/업데이터/헬퍼류이면 true.</summary>
    public static bool IsLauncherOrHelper(string? exeFileNameNoExt)
    {
        if (string.IsNullOrWhiteSpace(exeFileNameNoExt))
            return false;
        string n = exeFileNameNoExt.Trim().ToLowerInvariant();
        foreach (string k in Keywords)
            if (n.Contains(k))
                return true;
        return false;
    }
}
