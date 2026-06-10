namespace IPlaySpeed.App.Services;

/// <summary>
/// 알려진 일일퀘스트 게임 카탈로그. 감지 결과를 (1) 노이즈와 구분해 상단에 올리고,
/// (2) 보기 좋은 이름으로 정리하는 데 쓴다. 키워드는 소문자로 비교한다.
/// </summary>
public static class KnownGames
{
    public sealed record Known(string Display, string[] Keywords, string LauncherType, string[] ProcessHints);

    public static readonly Known[] Catalog =
    {
        new("Genshin Impact",        new[]{"genshin"},                         "hoyoplay", new[]{"genshinimpact","yuanshen"}),
        new("Honkai: Star Rail",     new[]{"star rail","starrail","honkai: star"}, "hoyoplay", new[]{"starrail"}),
        new("Zenless Zone Zero",     new[]{"zenless","zzz"},                   "hoyoplay", new[]{"zenlesszonezero"}),
        new("Honkai Impact 3rd",     new[]{"honkai impact"},                   "hoyoplay", new[]{"bh3"}),
        new("Wuthering Waves",       new[]{"wuthering"},                       "kuro",     new[]{"wuthering waves","client-win64-shipping"}),
        new("Punishing: Gray Raven", new[]{"gray raven","punishing"},          "kuro",     new[]{"pgr"}),
        new("NIKKE",                 new[]{"nikke","victory goddess","승리의 여신"}, "launcher", new[]{"nikke"}),
        new("Arknights: Endfield",   new[]{"endfield"},                        "launcher", new[]{"endfield"}),
        new("Arknights",             new[]{"arknights"},                       "launcher", new[]{"arknights"}),
        new("Reverse: 1999",         new[]{"reverse: 1999","reverse 1999","1999"}, "launcher", new[]{"reverse1999"}),
        new("Blue Archive",          new[]{"blue archive"},                    "launcher", new[]{"bluearchive"}),
        new("Lost Ark",              new[]{"lost ark","로스트아크"},          "steam",    new[]{"lostark"}),
        new("Ys / :Eternal etc.",    new[]{"이환","ys"},                       "launcher", Array.Empty<string>()),
    };

    /// <summary>이름에 카탈로그 키워드가 들어 있으면 매칭된 항목 반환, 없으면 null.</summary>
    public static Known? Match(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        string lower = name.ToLowerInvariant();
        foreach (var k in Catalog)
            foreach (var kw in k.Keywords)
                if (lower.Contains(kw.ToLowerInvariant()))
                    return k;
        return null;
    }

    /// <summary>설치 목록 노이즈(런타임/드라이버/도구 등)를 걸러내기 위한 제외 키워드.</summary>
    public static readonly string[] ExcludeKeywords =
    {
        "redistributable", "runtime", "directx", "vc++", "visual c++", ".net", "dotnet",
        "driver", "nvidia", "amd ", "realtek", "intel", "update", "sdk", "framework",
        "microsoft visual", "windows ", "python", "java", "node", "git ", "uninstall",
        "설치 제거", "재배포", "런타임", "드라이버",
    };

    public static bool LooksLikeNoise(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        string lower = name.ToLowerInvariant();
        foreach (var ex in ExcludeKeywords)
            if (lower.Contains(ex))
                return true;
        return false;
    }
}
