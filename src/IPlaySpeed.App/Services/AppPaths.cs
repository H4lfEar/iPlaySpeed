using System.IO;

namespace IPlaySpeed.App.Services;

/// <summary>데이터/아이콘 저장 위치. %AppData%\iPlaySpeed 아래에 모은다.</summary>
public static class AppPaths
{
    public static string DataDir
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "iPlaySpeed");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string IconsDir
    {
        get
        {
            string dir = Path.Combine(DataDir, "icons");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public const string GamesFile = "games.json";
    public const string SettingsFile = "settings.json";
    public const string StatesFile = "states.json";
    public const string PatchesFile = "patches.json";
    public const string HistoryFile = "history.json";

    /// <summary>감지 목록에서 사용자가 숨긴(게임 아님) 항목 키 목록.</summary>
    public const string HiddenFile = "hidden.json";

    /// <summary>게임별 최근 일별 실제 플레이 시간(초) 기록(완료 시간 자동 학습용).</summary>
    public const string PlayLogFile = "playlog.json";
}
