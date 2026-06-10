namespace IPlaySpeed.Core;

/// <summary>
/// 관측 기반 패치 주기 학습/예측/정렬. Python 참조 구현을 그대로 옮겼고 단위 테스트로 검증됨.
/// 사용자가 패치 일정을 수동 입력할 필요가 없도록, 게임 폴더 변화를 관측해 스스로 주기를 배운다.
/// </summary>
public static class PatchPredictor
{
    /// <summary>관측 패치가 2회 이상이면 실제 간격의 중앙값을, 아니면 기본값을 학습 주기로 사용.</summary>
    public static int LearnedIntervalDays(GamePatchInfo info, int defaultInterval)
    {
        var dates = info.ObservedPatchDates.OrderBy(d => d).ToList();
        if (dates.Count >= 2)
        {
            var gaps = new List<int>();
            for (int i = 1; i < dates.Count; i++)
                gaps.Add(dates[i].DayNumber - dates[i - 1].DayNumber);
            return (int)Math.Round(Median(gaps), MidpointRounding.ToEven);
        }
        return defaultInterval;
    }

    /// <summary>마지막 패치일. 관측값이 있으면 최신 관측, 없으면 폴더 수정일, 그것도 없으면 오늘.</summary>
    public static DateOnly LastPatchDate(GamePatchInfo info, DateOnly today)
    {
        if (info.ObservedPatchDates.Count > 0)
            return info.ObservedPatchDates.Max();
        if (info.LastFolderWrite is DateOnly w)
            return w;
        return today;
    }

    /// <summary>다음 패치까지 남은 일수 = (마지막패치 + 학습주기) - 오늘.</summary>
    public static int DaysUntilNextPatch(GamePatchInfo info, DateOnly today, int defaultInterval)
    {
        int interval = LearnedIntervalDays(info, defaultInterval);
        DateOnly predictedNext = LastPatchDate(info, today).AddDays(interval);
        return predictedNext.DayNumber - today.DayNumber;
    }

    /// <summary>
    /// 플레이 순서: '다음 패치까지 남은 일수가 많은'(=막 패치된, 한가한) 게임을 앞에,
    /// '임박한'(=백로그 많은) 게임을 뒤에. 동률이면 GameId로 안정 정렬.
    /// </summary>
    public static List<string> PlayOrder(
        IReadOnlyList<GamePatchInfo> infos, DateOnly today, int defaultInterval)
    {
        return infos
            .OrderByDescending(x => DaysUntilNextPatch(x, today, defaultInterval))
            .ThenBy(x => x.GameId, StringComparer.Ordinal)
            .Select(x => x.GameId)
            .ToList();
    }

    /// <summary>
    /// 폴더 용량이 의미있게 커졌으면(=대형 패치) true. 두 신호 중 하나라도 만족(OR):
    /// (a) 절대 증가량 ≥ minGrowthBytes(기본 200MB) → 대형 게임의 GB급 패치
    /// (b) 증가 비율 ≥ growthRatioThreshold(기본 3%)  → 소형 게임의 비례적 패치
    /// </summary>
    public static bool DetectPatchEvent(
        long prevTotalBytes, long newTotalBytes,
        double growthRatioThreshold = 0.03, long minGrowthBytes = 200L * 1024 * 1024)
    {
        if (prevTotalBytes <= 0)
            return false;
        long growth = newTotalBytes - prevTotalBytes;
        if (growth <= 0)
            return false;
        if (growth >= minGrowthBytes)
            return true;
        return (double)growth / prevTotalBytes >= growthRatioThreshold;
    }

    /// <summary>정렬된 정수 목록의 중앙값(짝수 개수면 가운데 두 값의 평균).</summary>
    private static double Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        if (n == 0) return 0;
        if (n % 2 == 1)
            return sorted[n / 2];
        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }
}
