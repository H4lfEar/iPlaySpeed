using System.Windows.Media.Imaging;
using IPlaySpeed.App.Native;

namespace IPlaySpeed.App.Models;

/// <summary>설치 감지로 찾은 게임 후보(아직 등록 전). 왼쪽 패널에 표시된다.</summary>
public sealed class GameCandidate
{
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string? InstallDir { get; set; }
    public string LauncherType { get; set; } = "none";

    /// <summary>탐지 출처(레지스트리/Steam/카탈로그) — 표시·디버깅용.</summary>
    public string Source { get; set; } = "";

    /// <summary>알려진 일일퀘스트 게임이면 true(상단에 우선 표시).</summary>
    public bool IsKnownDailyGame { get; set; }

    public string? IconPath { get; set; }

    private BitmapImage? _icon;
    private bool _iconTried;
    /// <summary>실행 파일 아이콘(지연 로드).</summary>
    public BitmapImage? Icon
    {
        get
        {
            if (!_iconTried)
            {
                _iconTried = true;
                if (IconPath is null && !string.IsNullOrEmpty(ExePath))
                {
                    string tmp = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "iPlaySpeed_" + System.IO.Path.GetFileNameWithoutExtension(ExePath) + ".png");
                    IconPath = IconExtractor.ExtractToPng(ExePath, tmp);
                }
                _icon = IconExtractor.LoadImage(IconPath);
            }
            return _icon;
        }
    }
}
