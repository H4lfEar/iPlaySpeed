namespace IPlaySpeed.Core;

/// <summary>
/// 일일 리셋 경계 판정. 사용자가 정한 하루 시작 시각을 기준으로 진행도를 초기화할지 결정한다.
/// Python 참조 구현을 그대로 옮겼고 단위 테스트로 검증됨.
/// </summary>
public static class DailyResetClock
{
    /// <summary>
    /// 아직 오늘자 리셋을 안 했고 지금이 리셋 시각을 지났으면 true.
    /// '리셋 기준 시각'을 하루의 시작으로 보고, 현재가 속한 리셋일의 경계를 구해
    /// 마지막 리셋이 그 경계 이전이면 리셋해야 한다.
    /// </summary>
    public static bool ShouldReset(DateTime lastReset, int resetHour, int resetMinute, DateTime now)
    {
        DateTime boundaryToday = new(now.Year, now.Month, now.Day, resetHour, resetMinute, 0);
        DateTime boundary = now >= boundaryToday ? boundaryToday : boundaryToday.AddDays(-1);
        return lastReset < boundary;
    }
}
