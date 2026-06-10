using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using IPlaySpeed.App.Models;

namespace IPlaySpeed.App.Services;

/// <summary>
/// 설치된 게임을 자동 감지한다. 출처: (1) Windows 제거(Uninstall) 레지스트리, (2) Steam 라이브러리.
/// 모든 접근은 방어적으로 try/catch 처리되어 실패해도 앱을 멈추지 않는다.
/// 무거운 작업이므로 호출자는 백그라운드 스레드에서 실행할 것.
/// </summary>
public static class GameDetector
{
    public static List<GameCandidate> DetectAll()
    {
        var found = new Dictionary<string, GameCandidate>(StringComparer.OrdinalIgnoreCase);
        try { ScanUninstallRegistry(found); } catch { /* 무시 */ }
        try { ScanSteam(found); } catch { /* 무시 */ }

        return found.Values
            .OrderByDescending(c => c.IsKnownDailyGame)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ── 레지스트리 제거 항목 스캔 ──
    private static void ScanUninstallRegistry(Dictionary<string, GameCandidate> found)
    {
        var roots = new (RegistryHive hive, RegistryView view)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser,  RegistryView.Registry64),
        };
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        foreach (var (hive, view) in roots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(path);
                if (uninstall is null) continue;

                foreach (string sub in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var k = uninstall.OpenSubKey(sub);
                        if (k is null) continue;

                        string? name = k.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(name) || KnownGames.LooksLikeNoise(name))
                            continue;
                        if ((k.GetValue("SystemComponent") as int?) == 1)
                            continue;

                        string? installLoc = k.GetValue("InstallLocation") as string;
                        string? displayIcon = k.GetValue("DisplayIcon") as string;

                        string? exe = ResolveExe(displayIcon, installLoc, name);
                        var known = KnownGames.Match(name);

                        // 알려진 게임이 아니면서 실행 파일도 못 찾으면 노이즈일 확률이 높아 건너뜀.
                        if (exe is null && known is null)
                            continue;

                        Add(found, new GameCandidate
                        {
                            Name = known?.Display ?? name.Trim(),
                            ExePath = exe ?? "",
                            InstallDir = installLoc,
                            LauncherType = known?.LauncherType ?? "none",
                            IsKnownDailyGame = known is not null,
                            Source = "registry"
                        });
                    }
                    catch { /* 한 항목 실패는 무시 */ }
                }
            }
            catch { /* 한 루트 실패는 무시 */ }
        }
    }

    // ── Steam 라이브러리 스캔 ──
    private static void ScanSteam(Dictionary<string, GameCandidate> found)
    {
        string? steam = SteamPath();
        if (steam is null) return;

        string libVdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        var libraries = new List<string> { steam };
        try
        {
            if (File.Exists(libVdf))
            {
                string text = File.ReadAllText(libVdf);
                foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                {
                    string p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p)) libraries.Add(p);
                }
            }
        }
        catch { /* 무시 */ }

        foreach (string lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string appsDir = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(appsDir)) continue;

            string[] manifests;
            try { manifests = Directory.GetFiles(appsDir, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (string acf in manifests)
            {
                try
                {
                    string text = File.ReadAllText(acf);
                    string? name = MatchVdf(text, "name");
                    string? installdir = MatchVdf(text, "installdir");
                    if (string.IsNullOrWhiteSpace(name) || KnownGames.LooksLikeNoise(name))
                        continue;

                    string? gameDir = installdir is null ? null
                        : Path.Combine(appsDir, "common", installdir);
                    string? exe = gameDir is not null && Directory.Exists(gameDir)
                        ? FindLikelyExe(gameDir, name) : null;

                    var known = KnownGames.Match(name);
                    if (exe is null && known is null)
                        continue;

                    Add(found, new GameCandidate
                    {
                        Name = known?.Display ?? name.Trim(),
                        ExePath = exe ?? "",
                        InstallDir = gameDir,
                        LauncherType = "steam",
                        IsKnownDailyGame = known is not null,
                        Source = "steam"
                    });
                }
                catch { /* 무시 */ }
            }
        }
    }

    private static string? SteamPath()
    {
        try
        {
            using var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            if (hkcu.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") is string p && Directory.Exists(p))
                return p;
        }
        catch { }
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            if (hklm.OpenSubKey(@"SOFTWARE\Valve\Steam")?.GetValue("InstallPath") is string p && Directory.Exists(p))
                return p;
        }
        catch { }
        return null;
    }

    // ── 보조 ──

    private static void Add(Dictionary<string, GameCandidate> found, GameCandidate c)
    {
        string key = !string.IsNullOrEmpty(c.ExePath) ? c.ExePath : c.Name;
        if (!found.ContainsKey(key))
            found[key] = c;
    }

    private static string? MatchVdf(string text, string field)
    {
        var m = Regex.Match(text, "\"" + field + "\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>DisplayIcon 또는 InstallLocation에서 실행할 만한 exe 경로를 찾는다.</summary>
    private static string? ResolveExe(string? displayIcon, string? installLoc, string name)
    {
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            string p = displayIcon.Trim().Trim('"');
            int comma = p.LastIndexOf(',');
            if (comma > 0 && p.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase) < comma)
                p = p[..comma];
            p = p.Trim().Trim('"');
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p)
                && !Path.GetFileName(p).Contains("unins", StringComparison.OrdinalIgnoreCase))
                return p;
        }
        if (!string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc))
            return FindLikelyExe(installLoc, name);
        return null;
    }

    /// <summary>폴더에서 게임/런처일 가능성이 높은 exe를 고른다(이름 일치 우선, 그다음 큰 파일).</summary>
    private static string? FindLikelyExe(string dir, string nameHint)
    {
        try
        {
            var exes = new List<FileInfo>();
            foreach (string f in SafeEnumerateExes(dir))
            {
                string fn = Path.GetFileName(f).ToLowerInvariant();
                if (fn.Contains("unins") || fn.Contains("crashpad") || fn.Contains("crashreport")
                    || fn.Contains("redist") || fn.Contains("setup") || fn.Contains("vcredist")
                    || fn.Contains("helper") || fn.Contains("dxsetup"))
                    continue;
                try { exes.Add(new FileInfo(f)); } catch { }
            }
            if (exes.Count == 0) return null;

            string[] tokens = nameHint.ToLowerInvariant()
                .Split(new[] { ' ', ':', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);

            // 1) "launcher" 우선
            var launcher = exes.FirstOrDefault(e =>
                Path.GetFileNameWithoutExtension(e.Name).ToLowerInvariant().Contains("launcher"));
            if (launcher is not null) return launcher.FullName;

            // 2) 이름 토큰이 들어간 exe
            var named = exes.FirstOrDefault(e =>
                tokens.Any(t => t.Length >= 3 &&
                    Path.GetFileNameWithoutExtension(e.Name).ToLowerInvariant().Contains(t)));
            if (named is not null) return named.FullName;

            // 3) 가장 큰 exe
            return exes.OrderByDescending(e => e.Length).First().FullName;
        }
        catch { return null; }
    }

    private static IEnumerable<string> SafeEnumerateExes(string dir)
    {
        var result = new List<string>();
        try { result.AddRange(Directory.GetFiles(dir, "*.exe")); } catch { }
        try
        {
            foreach (string sub in Directory.GetDirectories(dir))
            {
                try { result.AddRange(Directory.GetFiles(sub, "*.exe")); } catch { }
            }
        }
        catch { }
        return result;
    }
}
