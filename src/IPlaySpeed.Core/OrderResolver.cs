namespace IPlaySpeed.Core;

/// <summary>
/// 게임 표시/실행 순서를 결정한다. 자동 정렬이 켜져 있으면 패치 임박도(PatchPredictor)로,
/// 꺼져 있으면 사용자의 수동 순서(Order 필드)로 정렬한다.
/// 이 순서가 메인 목록·오버레이·"다음 게임" 실행에 모두 동일하게 적용된다.
/// </summary>
public static class OrderResolver
{
    /// <param name="games">(게임 id, 수동 Order) 목록.</param>
    /// <param name="patches">관측된 패치 이력(없는 게임은 콜드스타트로 처리).</param>
    /// <param name="autoSort">true=패치 자동 정렬, false=수동 Order 정렬.</param>
    /// <returns>정렬된 게임 id 목록.</returns>
    public static List<string> Resolve(
        IReadOnlyList<(string id, int order)> games,
        IReadOnlyList<GamePatchInfo> patches,
        bool autoSort,
        DateOnly today,
        int defaultInterval)
    {
        if (autoSort)
        {
            var byId = patches
                .GroupBy(p => p.GameId)
                .ToDictionary(g => g.Key, g => g.First());
            var infos = games
                .Select(g => byId.TryGetValue(g.id, out var p) ? p : new GamePatchInfo { GameId = g.id })
                .ToList();
            return PatchPredictor.PlayOrder(infos, today, defaultInterval);
        }

        return games
            .OrderBy(g => g.order)
            .ThenBy(g => g.id, StringComparer.Ordinal)
            .Select(g => g.id)
            .ToList();
    }
}
