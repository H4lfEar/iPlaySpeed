namespace IPlaySpeed.Core;

/// <summary>등록된 게임 1개의 영구 정보 (games.json 에 저장).</summary>
public sealed class GameEntry
{
    /// <summary>내부 식별자 (예: "zzz").</summary>
    public string Id { get; set; } = "";

    /// <summary>표시 이름 (예: "젠레스 존 제로").</summary>
    public string Name { get; set; } = "";

    /// <summary>추출한 아이콘 PNG 경로.</summary>
    public string? IconPath { get; set; }

    /// <summary>실행할 파일 경로. 런처가 있으면 런처 경로.</summary>
    public string ExePath { get; set; } = "";

    /// <summary>
    /// 완료 판정에 쓰는 '실제 게임' 프로세스 이름(확장자 제외).
    /// 런처와 게임 exe가 다를 때 사용. 비어 있으면 ExePath 의 파일명을 사용.
    /// </summary>
    public string? GameProcessName { get; set; }

    /// <summary>업데이트 감지를 위해 용량을 관측할 게임 설치 폴더.</summary>
    public string? GameFolder { get; set; }

    /// <summary>런처 종류 식별 (예: "hoyoplay", "steam", "none").</summary>
    public string LauncherType { get; set; } = "none";

    /// <summary>표시/실행 순서(자동 정렬 결과로 갱신될 수 있음).</summary>
    public int Order { get; set; }

    /// <summary>완료로 인정할 최소 활성 플레이 시간(분). 게임마다 다를 수 있음.</summary>
    public int ThresholdMinutes { get; set; } = 5;

    /// <summary>이 게임의 ExePath 파일명(확장자 제외)을 프로세스명으로 반환.</summary>
    public string EffectiveGameProcessName()
    {
        if (!string.IsNullOrWhiteSpace(GameProcessName))
            return GameProcessName!;
        try { return Path.GetFileNameWithoutExtension(ExePath); }
        catch { return ""; }
    }
}

/// <summary>하루 동안 한 게임의 누적 상태. 재실행을 넘어 같은 일일 런 내에서 유지.</summary>
public sealed class GameDailyState
{
    /// <summary>게임 창이 foreground였던 누적 초.</summary>
    public int ActivePlaySeconds { get; set; }

    /// <summary>이번 일일 런 중 게임 폴더 대형 변경(업데이트) 관측 여부.</summary>
    public bool UpdateDetectedToday { get; set; }

    /// <summary>수동 강제: true=완료, false=미완료, null=자동 판정.</summary>
    public bool? ManualOverride { get; set; }
}

/// <summary>한 게임의 관측된 패치 이력 (패치 주기 학습용, patches.json).</summary>
public sealed class GamePatchInfo
{
    public string GameId { get; set; } = "";

    /// <summary>관측된 대형 패치 발생일들.</summary>
    public List<DateOnly> ObservedPatchDates { get; set; } = new();

    /// <summary>콜드스타트용: 게임 폴더 최종 수정일.</summary>
    public DateOnly? LastFolderWrite { get; set; }

    /// <summary>업데이트 감지를 위한 직전 관측 폴더 총 용량(byte).</summary>
    public long LastTotalBytes { get; set; }
}

/// <summary>하루 단위 기록 (리더보드, history.json).</summary>
public sealed class DailyRecord
{
    public DateOnly Date { get; set; }
    public int CompletedGames { get; set; }
    public int TotalGames { get; set; }
    public int TotalMinutes { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
}

/// <summary>전역 설정 (settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>일일 런 시작(리셋) 시각 - 시.</summary>
    public int ResetHour { get; set; } = 6;

    /// <summary>일일 런 시작(리셋) 시각 - 분.</summary>
    public int ResetMinute { get; set; } = 0;

    /// <summary>패치 이력이 부족할 때 쓰는 기본 패치 주기(일).</summary>
    public int DefaultPatchIntervalDays { get; set; } = 42;

    /// <summary>true면 패치 주기로 자동 정렬, false면 사용자의 수동 정렬(Order)을 사용.</summary>
    public bool AutoSortByPatch { get; set; } = true;

    /// <summary>새 게임 등록 시 기본 완료 임계 시간(분).</summary>
    public int DefaultThresholdMinutes { get; set; } = 5;

    /// <summary>오버레이 토글 전역 단축키 (예: "Ctrl+Alt+I").</summary>
    public string OverlayHotkey { get; set; } = "Ctrl+Alt+I";

    /// <summary>다음 게임 실행 전역 단축키.</summary>
    public string NextGameHotkey { get; set; } = "Ctrl+Alt+N";

    /// <summary>마지막으로 일일 진행도를 리셋한 시각.</summary>
    public DateTime LastReset { get; set; } = DateTime.MinValue;

    /// <summary>런처 자동 클릭 기능 사용 여부(기본 꺼짐, 위험 동의 필요).</summary>
    public bool LauncherAutoClickEnabled { get; set; } = false;

    /// <summary>
    /// true면 게임이 포그라운드가 아니어도 '실행 중'이면 플레이 시간을 누적한다.
    /// (alt+tab 해두고 자동 진행하는 턴제 게임용. 기본 꺼짐 = 포그라운드일 때만 누적)
    /// </summary>
    public bool CountWhileRunning { get; set; } = false;

    /// <summary>true면 평소 플레이 시간을 학습해 게임별 완료 임계 시간을 자동 설정한다(기본 꺼짐).</summary>
    public bool AutoLearnThreshold { get; set; } = false;

    /// <summary>
    /// '다음' 버튼 동작: true면 현재 게임을 바로 종료한 뒤 다음 게임 실행(기본),
    /// false면 현재 게임을 종료하지 않고 다음 게임만 실행(여러 게임 동시 진행).
    /// </summary>
    public bool CloseOnNext { get; set; } = true;

    /// <summary>오버레이 마지막 위치 X.</summary>
    public double OverlayLeft { get; set; } = 40;

    /// <summary>오버레이 마지막 위치 Y.</summary>
    public double OverlayTop { get; set; } = 8;
}
