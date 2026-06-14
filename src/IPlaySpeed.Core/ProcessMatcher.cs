namespace IPlaySpeed.Core;

/// <summary>
/// 포그라운드/실행 중 프로세스 이름이 등록 게임과 일치하는지 판별.
/// 런처 타입 게임은 폴더 경로 매칭 실패 시 ProcessHints·GameProcessName 폴백에 사용한다.
/// </summary>
public static class ProcessMatcher
{
    /// <summary>
    /// foreground 프로세스명(소문자, 확장자 제외)이 이 게임과 일치하는지.
    /// effectiveName 정확 일치 → hints 부분 일치 순.
    /// </summary>
    public static bool MatchesForegroundName(
        string fgName,
        string? effectiveProcessName,
        IEnumerable<string>? processHints)
    {
        if (string.IsNullOrWhiteSpace(fgName))
            return false;

        if (!string.IsNullOrWhiteSpace(effectiveProcessName)
            && string.Equals(fgName, effectiveProcessName.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        if (processHints is null)
            return false;

        foreach (string hint in processHints)
        {
            if (string.IsNullOrWhiteSpace(hint))
                continue;
            if (fgName.Contains(hint.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>실행 파일 경로의 프로세스명(확장자 제외)이 게임과 일치하는지.</summary>
    public static bool MatchesExecutablePath(
        string fullPath,
        string? effectiveProcessName,
        IEnumerable<string>? processHints)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(fullPath);
            return MatchesForegroundName(name, effectiveProcessName, processHints);
        }
        catch
        {
            return false;
        }
    }
}
