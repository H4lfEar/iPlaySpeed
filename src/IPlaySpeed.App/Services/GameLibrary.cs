using System.Diagnostics;
using System.IO;
using IPlaySpeed.App.Native;
using IPlaySpeed.Core;

namespace IPlaySpeed.App.Services;

/// <summary>등록된 게임 목록을 관리(로드/저장/추가/삭제)한다.</summary>
public sealed class GameLibrary
{
    private readonly JsonStore _store;
    public List<GameEntry> Games { get; private set; }

    public GameLibrary(JsonStore store)
    {
        _store = store;
        Games = _store.Load(AppPaths.GamesFile, new List<GameEntry>());
        MigrateKnownGameMetadata();
    }

    /// <summary>기존 등록 게임에 카탈로그 ProcessHints·런처 프로세스명 보정.</summary>
    private void MigrateKnownGameMetadata()
    {
        bool changed = false;
        foreach (var g in Games)
        {
            var known = KnownGames.Match(g.Name);
            if (known is null) continue;
            if (g.ProcessHints.Count == 0 && known.ProcessHints.Length > 0)
            {
                g.ProcessHints = known.ProcessHints.ToList();
                changed = true;
            }
            if (!string.Equals(g.LauncherType, "none", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(g.GameProcessName)
                && LauncherFilter.IsLauncherOrHelper(g.GameProcessName))
            {
                g.GameProcessName = null;
                changed = true;
            }
            string? widened = WidenGameFolder(g.GameFolder);
            if (widened is not null && !string.Equals(g.GameFolder, widened, StringComparison.OrdinalIgnoreCase))
            {
                g.GameFolder = widened;
                changed = true;
            }
        }
        if (changed) Save();
    }

    /// <summary>등록 폴더가 .../Launcher 를 가리키면 상위(실제 게임 루트)로 넓힌다.</summary>
    private static string? WidenGameFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        try
        {
            string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!name.Equals("Launcher", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("launcher", StringComparison.OrdinalIgnoreCase))
                return null;
            string? parent = Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(parent) ? null : parent;
        }
        catch { return null; }
    }

    public void Save() => _store.Save(AppPaths.GamesFile, Games);

    /// <summary>
    /// 실행 파일을 받아 새 게임을 등록한다. 이름·아이콘을 자동 수집.
    /// 이미 같은 ExePath가 있으면 기존 항목을 반환.
    /// </summary>
    public GameEntry AddFromExe(string exePath)
    {
        var existing = Games.FirstOrDefault(
            g => string.Equals(g.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        return AddFromExeNamed(exePath, DeriveName(exePath), Path.GetDirectoryName(exePath), GuessLauncherType(exePath));
    }

    /// <summary>감지된 후보(이름·런처 정보 포함)로 등록. 이미 같은 ExePath면 기존 항목 반환.</summary>
    public GameEntry AddFromCandidate(Models.GameCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ExePath))
        {
            var existing = Games.FirstOrDefault(
                g => string.Equals(g.ExePath, candidate.ExePath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                return existing;
        }
        return AddFromExeNamed(candidate.ExePath, candidate.Name, candidate.InstallDir, candidate.LauncherType);
    }

    /// <summary>이름을 직접 지정해 등록. 이름이 비면 exe에서 유추.</summary>
    private GameEntry AddFromExeNamed(string exePath, string name, string? installDir, string launcherType)
    {
        string finalName = string.IsNullOrWhiteSpace(name) ? DeriveName(exePath) : name.Trim();
        string id = UniqueId(finalName);
        var known = KnownGames.Match(finalName);
        string lt = string.IsNullOrWhiteSpace(launcherType) ? "none" : launcherType;
        if (known is not null && !string.Equals(lt, "none", StringComparison.OrdinalIgnoreCase))
            lt = known.LauncherType;

        var entry = new GameEntry
        {
            Id = id,
            Name = finalName,
            ExePath = exePath ?? "",
            GameFolder = installDir ?? (string.IsNullOrEmpty(exePath) ? null : Path.GetDirectoryName(exePath)),
            GameProcessName = known is not null && !string.Equals(lt, "none", StringComparison.OrdinalIgnoreCase)
                ? null
                : (string.IsNullOrEmpty(exePath) ? null : Path.GetFileNameWithoutExtension(exePath)),
            Order = Games.Count,
            ThresholdMinutes = 5,
            LauncherType = lt,
            ProcessHints = known?.ProcessHints.ToList() ?? new List<string>()
        };

        if (!string.IsNullOrEmpty(exePath))
        {
            string iconPng = Path.Combine(AppPaths.IconsDir, id + ".png");
            entry.IconPath = IconExtractor.ExtractToPng(exePath, iconPng);
        }

        Games.Add(entry);
        Save();
        return entry;
    }

    public void Remove(GameEntry entry)
    {
        Games.Remove(entry);
        Save();
    }

    private static string DeriveName(string exePath)
    {
        if (string.IsNullOrEmpty(exePath))
            return "게임";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrWhiteSpace(info.ProductName))
                return info.ProductName!.Trim();
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
                return info.FileDescription!.Trim();
        }
        catch { /* 무시하고 파일명 사용 */ }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    private string UniqueId(string name)
    {
        string baseId = new string(name.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c)).ToArray());
        if (string.IsNullOrEmpty(baseId))
            baseId = "game";

        string id = baseId;
        int n = 2;
        while (Games.Any(g => g.Id == id))
            id = baseId + n++;
        return id;
    }

    private static string GuessLauncherType(string exePath)
    {
        string lower = exePath.ToLowerInvariant();
        if (lower.Contains("hoyoplay") || (lower.Contains("launcher") && lower.Contains("mihoyo")))
            return "hoyoplay";
        if (lower.Contains("steam"))
            return "steam";
        if (lower.Contains("launcher"))
            return "launcher";
        return "none";
    }
}
