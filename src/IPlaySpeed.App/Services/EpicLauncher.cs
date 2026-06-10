using System.IO;
using System.Text.Json;

namespace IPlaySpeed.App.Services;

/// <summary>
/// 에픽게임즈 런처로 설치된 게임은 게임 폴더의 exe(예: launcher_epic.exe)를 직접 실행하면
/// 정상 동작하지 않는다(에픽 런처가 에픽 컨텍스트로 자식 프로세스를 띄워야 함).
/// 반드시 com.epicgames.launcher:// URI 프로토콜로 실행해야 한다.
/// 에픽 매니페스트(%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item, JSON)를 읽어
/// '설치 위치 → 실행 URI'를 매핑한다. 에픽 미설치/매칭 실패 시 null을 돌려주어 일반 실행으로 폴백한다.
/// </summary>
public static class EpicLauncher
{
    private static string ManifestDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

    /// <summary>
    /// 주어진 게임 폴더 또는 실행파일이 에픽 설치 게임에 속하면 실행 URI를 반환한다.
    /// 매칭 기준: 게임 폴더가 매니페스트 InstallLocation과 같거나 그 하위, 또는 exe가 그 하위에 있을 때.
    /// </summary>
    public static string? ResolveLaunchUri(string? gameFolder, string? exePath)
    {
        try
        {
            if (!Directory.Exists(ManifestDir))
                return null;

            string? probe = NormalizeDir(gameFolder)
                ?? NormalizeDir(string.IsNullOrWhiteSpace(exePath) ? null : Path.GetDirectoryName(exePath));
            string? exeFull = SafeFull(exePath);
            if (probe is null && exeFull is null)
                return null;

            foreach (string file in Directory.GetFiles(ManifestDir, "*.item"))
            {
                string? loc, app;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    loc = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                    app = root.TryGetProperty("AppName", out var a) ? a.GetString() : null;
                }
                catch { continue; }

                if (string.IsNullOrWhiteSpace(loc) || string.IsNullOrWhiteSpace(app))
                    continue;

                string? locNorm = NormalizeDir(loc);
                if (locNorm is null) continue;

                bool match =
                    (probe is not null &&
                        (probe.Equals(locNorm, StringComparison.OrdinalIgnoreCase)
                         || probe.StartsWith(locNorm, StringComparison.OrdinalIgnoreCase)))
                    || (exeFull is not null && exeFull.StartsWith(locNorm, StringComparison.OrdinalIgnoreCase));

                if (match)
                    return $"com.epicgames.launcher://apps/{app}?action=launch&silent=true";
            }
        }
        catch { /* 매니페스트 접근 실패는 무시(일반 실행으로 폴백) */ }
        return null;
    }

    /// <summary>지정 폴더/실행파일이 에픽 게임인지 여부.</summary>
    public static bool IsEpicGame(string? gameFolder, string? exePath) =>
        ResolveLaunchUri(gameFolder, exePath) is not null;

    private static string? NormalizeDir(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; }
        catch { return null; }
    }

    private static string? SafeFull(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        try { return Path.GetFullPath(p); }
        catch { return null; }
    }
}
