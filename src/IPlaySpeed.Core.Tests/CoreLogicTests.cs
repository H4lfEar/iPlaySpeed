using IPlaySpeed.Core;
using Xunit;

namespace IPlaySpeed.Core.Tests;

/// <summary>
/// Python 참조 구현으로 먼저 검증한 시나리오를 C# 포팅본에 대해 다시 확인한다.
/// 모두 통과하면 C# 변환이 정확하다는 뜻. 실행: dotnet test
/// </summary>
public class CompletionJudgeTests
{
    private const int N = 5; // 5분

    [Fact]
    public void OneMinute_IsInProgress() =>
        Assert.Equal(GameStatus.InProgress,
            CompletionJudge.Judge(new GameDailyState { ActivePlaySeconds = 60 }, N));

    [Fact]
    public void ExactlyFiveMinutes_IsCompleted() =>
        Assert.Equal(GameStatus.Completed,
            CompletionJudge.Judge(new GameDailyState { ActivePlaySeconds = 300 }, N));

    [Fact]
    public void UpdateWithShortPlay_IsUpdateOnly() =>
        Assert.Equal(GameStatus.UpdateOnly,
            CompletionJudge.Judge(
                new GameDailyState { ActivePlaySeconds = 120, UpdateDetectedToday = true }, N));

    [Fact]
    public void FifteenMinuteUpdate_AccruesZeroPlay_ThenSixMinutesPlay_Completes()
    {
        var s = new GameDailyState { UpdateDetectedToday = true };
        for (int i = 0; i < 15 * 60; i++)
            CompletionJudge.TickForeground(s, gameWindowIsForeground: false);
        Assert.Equal(0, s.ActivePlaySeconds);
        Assert.Equal(GameStatus.UpdateOnly, CompletionJudge.Judge(s, N));

        for (int i = 0; i < 6 * 60; i++)
            CompletionJudge.TickForeground(s, gameWindowIsForeground: true);
        Assert.Equal(360, s.ActivePlaySeconds);
        Assert.Equal(GameStatus.Completed, CompletionJudge.Judge(s, N));
    }

    [Fact]
    public void AltTabbedSeconds_AreNotCounted()
    {
        var s = new GameDailyState();
        for (int i = 0; i < 600; i++)
            CompletionJudge.TickForeground(s, gameWindowIsForeground: !(i >= 100 && i < 200));
        Assert.Equal(500, s.ActivePlaySeconds);
        Assert.Equal(GameStatus.Completed, CompletionJudge.Judge(s, N));
    }

    [Fact]
    public void ManualOverride_True_Completes() =>
        Assert.Equal(GameStatus.Completed,
            CompletionJudge.Judge(new GameDailyState { ActivePlaySeconds = 10, ManualOverride = true }, N));

    [Fact]
    public void ManualOverride_False_NotCompleted() =>
        Assert.NotEqual(GameStatus.Completed,
            CompletionJudge.Judge(new GameDailyState { ActivePlaySeconds = 99999, ManualOverride = false }, N));

    [Fact]
    public void ProgressPercent_TwoOfFour_IsFifty()
    {
        var games = new List<(GameDailyState, int)>
        {
            (new GameDailyState { ActivePlaySeconds = 300 }, N),
            (new GameDailyState { ActivePlaySeconds = 300 }, N),
            (new GameDailyState { ActivePlaySeconds = 60 }, N),
            (new GameDailyState(), N),
        };
        Assert.Equal(50.0, CompletionJudge.ProgressPercent(games), 9);
    }

    [Fact]
    public void ProgressPercent_Empty_IsZero() =>
        Assert.Equal(0.0, CompletionJudge.ProgressPercent(new List<(GameDailyState, int)>()));

    [Fact]
    public void ProgressRatio_Half() =>
        Assert.Equal(0.5, CompletionJudge.ProgressRatio(new GameDailyState { ActivePlaySeconds = 150 }, 5), 9);

    [Fact]
    public void ProgressRatio_CapsAtOne() =>
        Assert.Equal(1.0, CompletionJudge.ProgressRatio(new GameDailyState { ActivePlaySeconds = 99999 }, 5), 9);

    [Fact]
    public void ProgressRatio_ManualComplete_IsOne() =>
        Assert.Equal(1.0, CompletionJudge.ProgressRatio(new GameDailyState { ManualOverride = true }, 5), 9);

    [Fact]
    public void ProgressRatio_Zero() =>
        Assert.Equal(0.0, CompletionJudge.ProgressRatio(new GameDailyState(), 5), 9);

    [Fact]
    public void Suggest_Empty_IsZero() =>
        Assert.Equal(0, CompletionJudge.SuggestThresholdMinutes(new List<int>()));

    [Fact]
    public void Suggest_210s_Is2min() => // 3분30초 → 168초 → 2분
        Assert.Equal(2, CompletionJudge.SuggestThresholdMinutes(new List<int> { 210 }));

    [Fact]
    public void Suggest_UsesMedian() => // 중앙값 300초 → 240초 → 4분
        Assert.Equal(4, CompletionJudge.SuggestThresholdMinutes(new List<int> { 60, 300, 1200 }));

    [Fact]
    public void Suggest_AtLeastOne() => // 짧아도 최소 1분
        Assert.Equal(1, CompletionJudge.SuggestThresholdMinutes(new List<int> { 30 }));
}

public class PatchPredictorTests
{
    private const int Default = 42;
    private static readonly DateOnly Today = new(2026, 6, 9);

    [Fact]
    public void OneObservation_UsesDefaultInterval() =>
        Assert.Equal(42, PatchPredictor.LearnedIntervalDays(
            new GamePatchInfo { ObservedPatchDates = { new DateOnly(2026, 5, 1) } }, Default));

    [Fact]
    public void Observations_LearnMedianInterval()
    {
        var d0 = new DateOnly(2026, 1, 1);
        var info = new GamePatchInfo
        {
            ObservedPatchDates = { d0, d0.AddDays(41), d0.AddDays(84), d0.AddDays(126) }
        };
        Assert.Equal(42, PatchPredictor.LearnedIntervalDays(info, 99));
    }

    [Fact]
    public void DaysUntilNextPatch_Is32()
    {
        var info = new GamePatchInfo
        {
            ObservedPatchDates = { new DateOnly(2026, 4, 18), new DateOnly(2026, 5, 30) }
        };
        Assert.Equal(42, PatchPredictor.LearnedIntervalDays(info, Default));
        Assert.Equal(32, PatchPredictor.DaysUntilNextPatch(info, Today, Default));
    }

    [Fact]
    public void PlayOrder_ImminentGoesLast()
    {
        var far = new GamePatchInfo { GameId = "far", ObservedPatchDates = { new DateOnly(2026, 6, 5) } };
        var soon = new GamePatchInfo { GameId = "soon", ObservedPatchDates = { new DateOnly(2026, 4, 29) } };
        var mid = new GamePatchInfo { GameId = "mid", ObservedPatchDates = { new DateOnly(2026, 5, 20) } };
        var order = PatchPredictor.PlayOrder(new[] { soon, mid, far }, Today, Default);
        Assert.Equal(new[] { "far", "mid", "soon" }, order);
    }

    [Fact]
    public void ColdStart_UsesFolderWriteDate()
    {
        var info = new GamePatchInfo { GameId = "cold", LastFolderWrite = new DateOnly(2026, 6, 1) };
        int expected = new DateOnly(2026, 6, 1).AddDays(42).DayNumber - Today.DayNumber;
        Assert.Equal(expected, PatchPredictor.DaysUntilNextPatch(info, Today, Default));
    }

    [Theory]
    [InlineData(40L * 1024 * 1024 * 1024, 41L * 1024 * 1024 * 1024, true)]
    [InlineData(40L * 1024 * 1024 * 1024, 40L * 1024 * 1024 * 1024 + 50L * 1024 * 1024, false)]
    [InlineData(40L * 1024 * 1024 * 1024, 40L * 1024 * 1024 * 1024, false)]
    [InlineData(0L, 5L * 1024 * 1024 * 1024, false)]
    public void DetectPatchEvent_Works(long prev, long now, bool expected) =>
        Assert.Equal(expected, PatchPredictor.DetectPatchEvent(prev, now));
}

public class OrderResolverTests
{
    private const int Default = 42;
    private static readonly DateOnly Today = new(2026, 6, 9);

    [Fact]
    public void Manual_FollowsOrderField()
    {
        var games = new (string, int)[] { ("zzz", 0), ("hsr", 1), ("wuwa", 2), ("nikke", 3) };
        var result = OrderResolver.Resolve(games, Array.Empty<GamePatchInfo>(), false, Today, Default);
        Assert.Equal(new[] { "zzz", "hsr", "wuwa", "nikke" }, result);
    }

    [Fact]
    public void Manual_SortsScrambledOrder()
    {
        var games = new (string, int)[] { ("a", 3), ("b", 1), ("c", 2), ("d", 0) };
        var result = OrderResolver.Resolve(games, Array.Empty<GamePatchInfo>(), false, Today, Default);
        Assert.Equal(new[] { "d", "b", "c", "a" }, result);
    }

    [Fact]
    public void Auto_ImminentPatchGoesLast()
    {
        var games = new (string, int)[] { ("zzz", 0), ("hsr", 1), ("wuwa", 2) };
        var patches = new[]
        {
            new GamePatchInfo { GameId = "zzz", ObservedPatchDates = { new DateOnly(2026, 6, 5) } },
            new GamePatchInfo { GameId = "hsr", ObservedPatchDates = { new DateOnly(2026, 4, 29) } },
            new GamePatchInfo { GameId = "wuwa", ObservedPatchDates = { new DateOnly(2026, 5, 20) } },
        };
        var result = OrderResolver.Resolve(games, patches, true, Today, Default);
        Assert.Equal(new[] { "zzz", "wuwa", "hsr" }, result);
    }

    [Fact]
    public void Auto_IncludesGamesWithoutPatchInfo()
    {
        var games = new (string, int)[] { ("zzz", 0), ("new", 9) };
        var patches = new[] { new GamePatchInfo { GameId = "zzz", ObservedPatchDates = { new DateOnly(2026, 6, 5) } } };
        var result = OrderResolver.Resolve(games, patches, true, Today, Default);
        Assert.Equal(2, result.Count);
        Assert.Contains("zzz", result);
        Assert.Contains("new", result);
    }
}

public class DailyResetClockTests
{
    [Fact]
    public void YesterdayReset_TodayPastBoundary_Resets() =>
        Assert.True(DailyResetClock.ShouldReset(
            new DateTime(2026, 6, 8, 7, 0, 0), 6, 30, new DateTime(2026, 6, 9, 8, 0, 0)));

    [Fact]
    public void AlreadyResetToday_DoesNotReset() =>
        Assert.False(DailyResetClock.ShouldReset(
            new DateTime(2026, 6, 9, 6, 30, 0), 6, 30, new DateTime(2026, 6, 9, 10, 0, 0)));

    [Fact]
    public void EarlyMorning_ResetYesterday_DoesNotReset() =>
        Assert.False(DailyResetClock.ShouldReset(
            new DateTime(2026, 6, 8, 6, 30, 0), 6, 30, new DateTime(2026, 6, 9, 3, 0, 0)));

    [Fact]
    public void EarlyMorning_LastResetTwoDaysAgo_Resets() =>
        Assert.True(DailyResetClock.ShouldReset(
            new DateTime(2026, 6, 7, 6, 30, 0), 6, 30, new DateTime(2026, 6, 9, 3, 0, 0)));

    [Fact]
    public void ExactlyAtBoundary_Resets() =>
        Assert.True(DailyResetClock.ShouldReset(
            new DateTime(2026, 6, 8, 6, 30, 0), 6, 30, new DateTime(2026, 6, 9, 6, 30, 0)));
}

public class LauncherFilterTests
{
    [Theory]
    [InlineData("launcher_epic")]
    [InlineData("HYP")]
    [InlineData("HYPHelper")]
    [InlineData("HYUpdater")]
    [InlineData("hpatchz")]
    [InlineData("crashreport")]
    [InlineData("nikke_launcher")]
    [InlineData("NTEGlobalLauncher")]
    public void Launchers_AreFiltered(string name) =>
        Assert.True(LauncherFilter.IsLauncherOrHelper(name));

    [Theory]
    [InlineData("ZenlessZoneZero")]
    [InlineData("StarRail")]
    [InlineData("GenshinImpact")]
    [InlineData("Wuthering Waves")]
    public void RealGames_AreNotFiltered(string name) =>
        Assert.False(LauncherFilter.IsLauncherOrHelper(name));

    [Fact]
    public void Empty_IsNotFiltered() =>
        Assert.False(LauncherFilter.IsLauncherOrHelper(""));
}

public class ProcessMatcherTests
{
    [Fact]
    public void Hint_SubstringMatches() =>
        Assert.True(ProcessMatcher.MatchesForegroundName("nikke-win64-shipping", null, new[] { "nikke" }));

    [Fact]
    public void Unrelated_DoesNotMatch() =>
        Assert.False(ProcessMatcher.MatchesForegroundName("chrome", null, new[] { "nikke" }));

    [Fact]
    public void EffectiveName_ExactMatch() =>
        Assert.True(ProcessMatcher.MatchesForegroundName("genshinimpact", "genshinimpact", Array.Empty<string>()));
}

public class ActionCompletionTests
{
    [Fact]
    public void ActionMode_StillAutoCompletesByTime() =>
        Assert.Equal(GameStatus.Completed,
            CompletionJudge.Judge(new GameDailyState { ActivePlaySeconds = 360 }, 6, actionBased: true));

    [Fact]
    public void ActionMode_ManualComplete() =>
        Assert.Equal(GameStatus.Completed,
            CompletionJudge.Judge(new GameDailyState { ManualOverride = true }, 5, actionBased: true));

    [Fact]
    public void ActionMode_ProgressRatio_FollowsTime() =>
        Assert.Equal(0.5, CompletionJudge.ProgressRatio(new GameDailyState { ActivePlaySeconds = 180 }, 6, actionBased: true), 9);
}
