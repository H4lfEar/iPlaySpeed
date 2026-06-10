using System.Diagnostics;
using Windows.Foundation;
using Windows.Media.Control;

namespace IPlaySpeed.App.Services;

/// <summary>
/// Windows 시스템 미디어 컨트롤(SMTC)을 통해 현재 재생 중인 앱(Spotify, 브라우저의 YouTube 등)을
/// 이전/재생·정지/다음으로 제어하고, 제목·재생시간·출처 앱을 가져온다.
/// SMTC에 연동된 앱만 제어 가능하며, 일반 사용자 세션에서 실행해야 동작한다.
/// </summary>
public sealed class MediaController
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    // 재생 위치는 SMTC가 띄엄띄엄 보고하므로, 마지막 보고값 + 경과시간으로 보간해 매초 부드럽게 표시한다.
    private TimeSpan _reportedPos;
    private DateTimeOffset _lastUpdated;
    private bool _isPlaying;

    /// <summary>"제목 — 아티스트" 문자열.</summary>
    public string CurrentTitle { get; private set; } = "";

    /// <summary>전체 길이(알 수 없으면 0).</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>재생 출처 앱 식별자(예: "Spotify.exe", "Chrome", AUMID).</summary>
    public string SourceAppId { get; private set; } = "";

    /// <summary>보간된 현재 재생 위치(매초 호출해도 1초씩 증가).</summary>
    public TimeSpan Position
    {
        get
        {
            var pos = _reportedPos;
            if (_isPlaying && _lastUpdated != default)
                pos += DateTimeOffset.Now - _lastUpdated;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
            return pos;
        }
    }

    public bool HasSession
    {
        get { try { return _manager?.GetCurrentSession() is not null; } catch { return false; } }
    }

    public async Task InitAsync()
    {
        try { _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); }
        catch { _manager = null; }
    }

    /// <summary>제목·재생시간·출처를 갱신. 세션이 없으면 모두 비움.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session is null)
            {
                CurrentTitle = ""; SourceAppId = "";
                _reportedPos = TimeSpan.Zero; Duration = TimeSpan.Zero; _isPlaying = false;
                return;
            }

            SourceAppId = session.SourceAppUserModelId ?? "";

            try
            {
                var tl = session.GetTimelineProperties();
                _reportedPos = tl.Position;
                _lastUpdated = tl.LastUpdatedTime;
                Duration = tl.EndTime;
            }
            catch { _reportedPos = TimeSpan.Zero; Duration = TimeSpan.Zero; _lastUpdated = default; }

            try
            {
                var pb = session.GetPlaybackInfo();
                _isPlaying = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch { _isPlaying = false; }

            var p = await session.TryGetMediaPropertiesAsync();
            string title = p?.Title ?? "";
            string artist = p?.Artist ?? "";
            CurrentTitle = string.IsNullOrWhiteSpace(artist) ? title : $"{title} — {artist}";
        }
        catch
        {
            CurrentTitle = ""; SourceAppId = "";
            _reportedPos = TimeSpan.Zero; Duration = TimeSpan.Zero; _isPlaying = false;
        }
    }

    public void Next() => Invoke(s => s.TrySkipNextAsync());
    public void Previous() => Invoke(s => s.TrySkipPreviousAsync());
    public void PlayPause() => Invoke(s => s.TryTogglePlayPauseAsync());

    private async void Invoke(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> action)
    {
        try
        {
            var session = _manager?.GetCurrentSession();
            if (session is not null)
                await action(session);
        }
        catch { /* 제어 실패는 무시(연동 안 된 앱 등) */ }
    }

    /// <summary>
    /// 출처 앱의 실행 파일 경로를 best-effort로 찾는다(아이콘 추출용).
    /// 주의: 브라우저에서 재생되는 YouTube는 브라우저(Chrome/Edge) 아이콘으로 표시된다.
    /// SMTC는 "유튜브"인지까지는 구분해 주지 않는다.
    /// </summary>
    public static string? ResolveSourceExe(string appId)
    {
        string? token = ResolveSourceProcessName(appId);
        if (token is null) return null;

        try
        {
            foreach (var pr in Process.GetProcessesByName(token))
            {
                try
                {
                    string? f = pr.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(f)) return f;
                }
                catch { /* 권한 등 */ }
                finally { pr.Dispose(); }
            }
        }
        catch { /* 무시 */ }
        return null;
    }

    /// <summary>
    /// 출처 앱 식별자(AUMID 또는 "Spotify.exe")에서 프로세스명(확장자 제외)을 추출한다.
    /// 볼륨 분리 시 미디어 앱의 오디오 세션을 찾는 데 쓴다. ResolveSourceExe와 동일한 토큰 규칙.
    /// (한계: UWP 앱의 AUMID처럼 경로/확장자가 없는 식별자는 프로세스명과 다를 수 있음)
    /// </summary>
    public static string? ResolveSourceProcessName(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return null;

        string token = appId;
        int slash = token.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) token = token[(slash + 1)..];
        if (token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) token = token[..^4];
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
