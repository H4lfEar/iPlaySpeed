namespace IPlaySpeed.Core;

/// <summary>
/// 완료 판정 로직. Python 참조 구현(verify/logic_reference.py)을 그대로 옮긴 것으로,
/// 28개 단위 테스트로 알고리즘이 검증되었다. Windows API에 의존하지 않는 순수 계산이다.
/// </summary>
public static class CompletionJudge
{
    /// <summary>
    /// 핵심 규칙:
    /// - 수동 오버라이드가 최우선.
    /// - 활성 플레이(게임 창 foreground 누적)가 임계치 이상이면 완료.
    /// - 업데이트가 감지됐는데 플레이가 모자라면 UpdateOnly(껐다 켰을 뿐 = 미완료).
    /// </summary>
    public static GameStatus Judge(GameDailyState s, int thresholdMinutes, bool actionBased = false)
    {
        if (s.ManualOverride == true)
            return GameStatus.Completed;
        if (s.ManualOverride == false)
            return s.ActivePlaySeconds > 0 ? GameStatus.InProgress : GameStatus.NotStarted;

        int threshold = thresholdMinutes * 60;
        if (s.ActivePlaySeconds >= threshold)
            return GameStatus.Completed;
        if (s.UpdateDetectedToday && s.ActivePlaySeconds < threshold)
            return GameStatus.UpdateOnly;
        if (s.ActivePlaySeconds > 0)
            return GameStatus.InProgress;
        return GameStatus.NotStarted;
    }

    /// <summary>
    /// 1초 타이머가 호출. 게임 창이 맨 앞일 때만 시간이 쌓인다.
    /// 업데이트 중(런처/업데이터가 foreground)에는 누적되지 않아 오판정을 막는다.
    /// </summary>
    public static void TickForeground(GameDailyState s, bool gameWindowIsForeground, int seconds = 1)
    {
        if (gameWindowIsForeground)
            s.ActivePlaySeconds += seconds;
    }

    /// <summary>
    /// 한 게임의 완료까지 진행 비율(0~1). 아이콘 '차오름' 표시에 사용한다.
    /// 수동 완료=1, 임계 시간 대비 누적 플레이 시간의 비율(최대 1).
    /// </summary>
    public static double ProgressRatio(GameDailyState s, int thresholdMinutes, bool actionBased = false)
    {
        if (s.ManualOverride == true)
            return 1.0;
        int threshold = thresholdMinutes * 60;
        if (threshold <= 0)
            return s.ActivePlaySeconds > 0 ? 1.0 : 0.0;
        double r = (double)s.ActivePlaySeconds / threshold;
        return r < 0 ? 0.0 : r > 1 ? 1.0 : r;
    }

    /// <summary>
    /// 최근 일별 실제 플레이 시간(초) 기록으로부터 '완료 임계 시간(분)'을 추천한다.
    /// 평소 플레이의 중앙값에 안전계수(0.8)를 곱해, 평균보다 조금 일찍 완료되도록 한다.
    /// 기록이 없으면 0(추천 없음). 최소 1분.
    /// 예: 평소 3분30초(210초) → 168초 → 2분.
    /// </summary>
    public static int SuggestThresholdMinutes(IReadOnlyList<int> recentPlaySeconds)
    {
        if (recentPlaySeconds is null || recentPlaySeconds.Count == 0)
            return 0;

        var sorted = recentPlaySeconds.Where(s => s > 0).OrderBy(s => s).ToList();
        if (sorted.Count == 0)
            return 0;

        double median = sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2.0;

        int minutes = (int)Math.Floor(median * 0.8 / 60.0);
        return Math.Max(1, minutes);
    }

    /// <summary>전체 진행도(%) = 완료된 게임 수 / 전체 게임 수.</summary>
    public static double ProgressPercent(
        IReadOnlyList<(GameDailyState state, int thresholdMinutes)> games,
        bool actionBased = false)
    {
        if (games.Count == 0)
            return 0.0;
        int done = 0;
        foreach (var (state, threshold) in games)
            if (Judge(state, threshold, actionBased) == GameStatus.Completed)
                done++;
        return (double)done / games.Count * 100.0;
    }
}
