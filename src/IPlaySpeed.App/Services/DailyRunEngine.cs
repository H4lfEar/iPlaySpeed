using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using IPlaySpeed.App.Native;
using IPlaySpeed.App.ViewModels;
using IPlaySpeed.Core;

namespace IPlaySpeed.App.Services;

/// <summary>
/// 일일 런의 심장. 1초 타이머로 (1) 리셋 경계 확인, (2) 게임 창 포그라운드 시간 누적,
/// (3) 진행도 갱신, (4) 현재 게임 추적을 수행하고, 게임 실행/종료/다음게임/정렬을 제어한다.
/// 판정·예측·정렬은 검증된 IPlaySpeed.Core를 호출한다.
/// </summary>
public sealed class DailyRunEngine : INotifyPropertyChanged, IDisposable
{
    private readonly GameLibrary _library;
    private readonly JsonStore _store;
    private readonly AppSettings _settings;

    private readonly Dictionary<string, GameDailyState> _states;
    private readonly Dictionary<string, GamePatchInfo> _patches;
    private readonly object _patchLock = new();
    private readonly DispatcherTimer _timer;

    private int _saveCounter;
    private bool _dayRecorded;

    /// <summary>지금 화면 맨 앞(포그라운드)으로 잡힌 '실제 플레이 중' 게임. 없으면 null.</summary>
    private GameRow? _currentRow;

    /// <summary>
    /// 가장 최근에 포그라운드였던 게임. 오버레이(Topmost) 등 우리 앱 창을 클릭하면 게임이
    /// 포그라운드를 잃어 _currentRow가 null이 되므로, 다음게임/볼륨 대상은 이 값으로 판정한다.
    /// </summary>
    private GameRow? _lastGameRow;

    /// <summary>진행도 리셋 직후 '다음'을 누르면 실행 중 게임을 무시하고 맨 위부터 시작하게 하는 1회성 플래그.</summary>
    private bool _restartFromTop;

    /// <summary>오버레이 상단에 표시할 게임(포그라운드 또는 백그라운드 카운트 중인 직전 게임).</summary>
    private GameRow? _displayRow;

    /// <summary>게임별 최근 일별 실제 플레이 시간(초) 기록(완료 시간 자동 학습용).</summary>
    private readonly Dictionary<string, List<int>> _playLog;

    // 백그라운드 카운트·실행 감지용 프로세스 경로 스뺅샷(백그라운드 스레드에서 주기 갱신).
    private volatile List<string>? _cachedRunningPaths;
    private bool _snapshotBusy;
    private int _snapshotTick;

    /// <summary>직전 틱의 게임별 실행 여부(종료→완료 감지용).</summary>
    private readonly Dictionary<string, bool> _wasRunning = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>앱이 CloseGame으로 종료 중인 게임(종료 완료 시 완료 처리 스킵).</summary>
    private readonly HashSet<string> _skipExitComplete = new(StringComparer.OrdinalIgnoreCase);

    private bool ActionBased => _settings.ActionBasedCompletion;

    public ObservableCollection<GameRow> Rows { get; } = new();

    public DailyRunEngine(GameLibrary library, JsonStore store, AppSettings settings)
    {
        _library = library;
        _store = store;
        _settings = settings;

        _states = store.Load(AppPaths.StatesFile, new Dictionary<string, GameDailyState>());
        _patches = LoadPatches(store);
        _playLog = store.Load(AppPaths.PlayLogFile, new Dictionary<string, List<int>>());

        foreach (var game in _library.Games.OrderBy(g => g.Order))
        {
            var state = GetOrCreateState(game.Id);
            Rows.Add(new GameRow(game, state));
        }

        if (_settings.AutoLearnThreshold)
            ApplyLearnedThresholds(); // 시작 시 기존 기록으로 임계 시간 반영

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
    }

    /// <summary>학습된 평소 플레이 시간으로 게임별 완료 임계 시간을 갱신한다(AutoLearnThreshold 사용 시).</summary>
    public void ApplyLearnedThresholds()
    {
        bool changed = false;
        foreach (var row in Rows)
        {
            if (row.Entry.ThresholdUserSet) continue;
            if (!_playLog.TryGetValue(row.Entry.Id, out var list)) continue;
            int suggested = CompletionJudge.SuggestThresholdMinutes(list);
            if (suggested > 0 && row.Entry.ThresholdMinutes != suggested)
            {
                row.ThresholdMinutes = suggested; // 1~600 보정 + 변경 알림
                changed = true;
            }
        }
        if (changed) { _library.Save(); foreach (var r in Rows) r.Refresh(ActionBased); }
    }

    /// <summary>하루 마감 시 각 게임의 실제 플레이 시간(초)을 기록하고(최근 14일), 학습 임계치를 갱신한다.</summary>
    private void RecordDailyPlayAndLearn()
    {
        bool changed = false;
        foreach (var row in Rows)
        {
            int secs = row.State.ActivePlaySeconds;
            if (secs <= 0) continue;
            if (!_playLog.TryGetValue(row.Entry.Id, out var list))
            {
                list = new List<int>();
                _playLog[row.Entry.Id] = list;
            }
            list.Add(secs);
            if (list.Count > 14) list.RemoveRange(0, list.Count - 14);
            changed = true;
        }
        if (changed) _store.Save(AppPaths.PlayLogFile, _playLog);
        if (_settings.AutoLearnThreshold) ApplyLearnedThresholds();
    }

    public void Start()
    {
        ApplyOrder();
        _timer.Start();
    }

    /// <summary>등록 직후의 게임을 런타임 목록에 추가하고 행을 반환.</summary>
    public GameRow AddGame(GameEntry entry)
    {
        var state = GetOrCreateState(entry.Id);
        var row = new GameRow(entry, state);
        Rows.Add(row);
        SaveStates();
        ApplyOrder();
        return row;
    }

    /// <summary>게임을 런타임 목록과 저장 상태에서 제거.</summary>
    public void RemoveRow(GameRow row)
    {
        Rows.Remove(row);
        if (_lastGameRow == row) _lastGameRow = null;
        if (_currentRow == row) _currentRow = null;
        _states.Remove(row.Entry.Id);
        lock (_patchLock)
            _patches.Remove(row.Entry.Id);
        SaveStates();
        SavePatches();
    }

    // ── 정렬(자동/수동) ──

    /// <summary>설정에 따라 Rows를 자동(패치 임박도) 또는 수동(Order) 순서로 재배열.</summary>
    public void ApplyOrder()
    {
        var pairs = Rows.Select(r => (r.Entry.Id, r.Entry.Order)).ToList();
        List<GamePatchInfo> patchList;
        lock (_patchLock)
            patchList = _patches.Values.ToList();

        var order = OrderResolver.Resolve(
            pairs, patchList, _settings.AutoSortByPatch,
            DateOnly.FromDateTime(DateTime.Now), _settings.DefaultPatchIntervalDays);

        for (int i = 0; i < order.Count; i++)
        {
            int cur = IndexOfRow(order[i]);
            if (cur >= 0 && cur != i)
                Rows.Move(cur, i);
        }
    }

    /// <summary>자동 정렬 on/off. UI 체크박스가 양방향 바인딩.</summary>
    public bool AutoSortByPatch
    {
        get => _settings.AutoSortByPatch;
        set
        {
            if (_settings.AutoSortByPatch == value) return;
            _settings.AutoSortByPatch = value;
            _store.Save(AppPaths.SettingsFile, _settings);
            if (value)
                ApplyOrder();              // 자동: 패치 예측대로 재정렬
            else
                PersistManualOrder();      // 수동: 현재 보이는 순서를 그대로 고정(점프 방지)
            OnChanged(nameof(AutoSortByPatch));
            OnChanged(nameof(ManualSortEnabled));
        }
    }

    /// <summary>수동 정렬(▲▼) 가능 여부 = 자동 정렬이 꺼져 있을 때.</summary>
    public bool ManualSortEnabled => !_settings.AutoSortByPatch;

    public void MoveUp(GameRow row)
    {
        EnsureManualSort();
        int i = Rows.IndexOf(row);
        if (i > 0) { Rows.Move(i, i - 1); PersistManualOrder(); }
    }

    public void MoveDown(GameRow row)
    {
        EnsureManualSort();
        int i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1) { Rows.Move(i, i + 1); PersistManualOrder(); }
    }

    /// <summary>▲▼를 누르면 자동 정렬을 끄고 현재 순서를 고정해 수동 정렬로 전환한다.</summary>
    private void EnsureManualSort()
    {
        if (_settings.AutoSortByPatch)
            AutoSortByPatch = false; // 세터가 현재 순서를 Order에 고정하고 체크박스도 갱신
    }

    private void PersistManualOrder()
    {
        for (int i = 0; i < Rows.Count; i++)
            Rows[i].Entry.Order = i;
        _library.Save();
    }

    private int IndexOfRow(string id)
    {
        for (int i = 0; i < Rows.Count; i++)
            if (Rows[i].Entry.Id == id) return i;
        return -1;
    }

    /// <summary>게임별 완료 임계 시간(분) 변경. 즉시 판정·오버레이에 반영.</summary>
    public void SetThreshold(GameRow row, int minutes)
    {
        row.ThresholdMinutes = minutes;
        row.Entry.ThresholdUserSet = true;
        _library.Save();
        row.Refresh(ActionBased);
        NotifyProgressChanged();
        OnChanged(nameof(ProgressPercent));
        VisualStateChanged?.Invoke();
    }

    /// <summary>임계값·완료 상태 등 시각 표시 갱신이 필요할 때(오버레이 구독).</summary>
    public event Action? VisualStateChanged;

    // ── 진행도 ──
    private double _progressPercent;
    public double ProgressPercent
    {
        get => _progressPercent;
        private set { if (Math.Abs(_progressPercent - value) > 0.001) { _progressPercent = value; OnChanged(nameof(ProgressPercent)); OnChanged(nameof(ProgressText)); } }
    }
    public string ProgressText => $"{ProgressPercent:0}%  ({CompletedCount}/{Rows.Count})";
    public int CompletedCount => Rows.Count(r => r.IsCompleted);

    private string _currentGameName = "대기 중";
    public string CurrentGameName
    {
        get => _currentGameName;
        private set { if (_currentGameName != value) { _currentGameName = value; OnChanged(nameof(CurrentGameName)); } }
    }

    /// <summary>볼륨 조절 대상 게임(가장 최근 포그라운드였던 게임). 없으면 null.</summary>
    public GameEntry? CurrentEntry => _lastGameRow?.Entry;

    /// <summary>지금 게임이 실제로 포그라운드인지(플레이 중 표시·시간 표시용).</summary>
    public bool IsGameForeground => _currentRow is not null;

    /// <summary>현재 포그라운드 게임의 오늘 누적 플레이 초. 없으면 0.</summary>
    public int CurrentPlaySeconds => _currentRow?.State.ActivePlaySeconds ?? 0;

    /// <summary>오버레이 상단에 표시할 게임이 있는지(포그라운드 또는 백그라운드 카운트 중인 직전 게임).</summary>
    public bool HasDisplayGame => _displayRow is not null;

    /// <summary>표시용 게임 이름(없으면 대기 안내).</summary>
    public string DisplayGameName => _displayRow?.Name ?? (Rows.Count == 0 ? "등록된 게임 없음" : "대기 중");

    /// <summary>표시용 게임의 오늘 누적 플레이 초.</summary>
    public int DisplayPlaySeconds => _displayRow?.State.ActivePlaySeconds ?? 0;

    /// <summary>다음에 플레이할 대기 게임(표시 순서상 첫 미완료) 이름. 없으면 안내 문구.</summary>
    public string NextWaitingGameName
    {
        get
        {
            var next = Rows.FirstOrDefault(r => !r.IsCompleted);
            if (next is not null) return next.Name;
            return Rows.Count == 0 ? "등록된 게임 없음" : "모두 완료 🎉";
        }
    }

    // ── 1초 틱 ──
    private void OnTick(object? sender, EventArgs e)
    {
        if (DailyResetClock.ShouldReset(_settings.LastReset, _settings.ResetHour, _settings.ResetMinute, DateTime.Now))
            ResetDay();

        var (fg, fgPath) = ForegroundWatcher.ForegroundProcessInfo();

        // 실행 중 프로세스 경로는 항상 ~2초마다 갱신(다중 실행·종료 감지·백그라운드 카운트에 사용).
        List<string>? runningPaths = _cachedRunningPaths;
        if (!_snapshotBusy && (_cachedRunningPaths is null || ++_snapshotTick >= 2))
        {
            _snapshotTick = 0;
            _snapshotBusy = true;
            Task.Run(() => SnapshotProcessPaths())
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully) _cachedRunningPaths = t.Result;
                    _snapshotBusy = false;
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        GameRow? current = null;
        foreach (var row in Rows)
        {
            bool isForeground = MatchesForeground(row.Entry, fg, fgPath);
            bool isRunning = runningPaths is not null && IsRealGameRunning(row.Entry, runningPaths);
            bool count = isForeground
                || (_settings.CountWhileRunning && isRunning);

            if (ActionBased)
            {
                if (_wasRunning.TryGetValue(row.Entry.Id, out bool wasRunning) && wasRunning && !isRunning)
                {
                    if (_skipExitComplete.Remove(row.Entry.Id))
                    { /* 앱이 닫은 경우 — Next에서 이미 완료 처리 */ }
                    else if (row.State.ManualOverride != true)
                        MarkComplete(row);
                }
                _wasRunning[row.Entry.Id] = isRunning;
            }

            CompletionJudge.TickForeground(row.State, count);
            if (isForeground)
                current = row;
            row.Refresh(ActionBased);
        }

        var pairs = Rows.Select(r => (r.State, r.Entry.ThresholdMinutes)).ToList();
        ProgressPercent = CompletionJudge.ProgressPercent(pairs, ActionBased);

        foreach (var row in Rows)
            row.IsCurrent = row == current;
        _currentRow = current;
        if (current is not null)
            _lastGameRow = current; // 우리 앱 창 클릭으로 포그라운드를 잃어도 '직전 게임'은 유지
        CurrentGameName = current?.Name ?? (Rows.Count == 0 ? "등록된 게임 없음" : "대기 중");

        // 표시용: 포그라운드 게임이 있으면 그것을, 없더라도 '백그라운드 카운트'가 켜져 있고 직전 게임이
        // 아직 실행 중이면 그 게임을 계속 표시한다(alt+tab 자동진행 시 '대기 중'으로 헷갈리지 않게).
        _displayRow = current;
        if (_displayRow is null && _settings.CountWhileRunning && _lastGameRow is not null
            && runningPaths is not null && IsRealGameRunning(_lastGameRow.Entry, runningPaths))
            _displayRow = _lastGameRow;

        if (Rows.Count > 0 && CompletedCount == Rows.Count && !_dayRecorded)
            RecordDayComplete();

        if (++_saveCounter >= 15)
        {
            _saveCounter = 0;
            SaveStates();
        }
    }

    // ── 게임 실행/종료/다음 ──

    /// <summary>게임(또는 런처)을 실행한다. 실행 전 패치 여부를 백그라운드로 점검.</summary>
    public void LaunchGame(GameEntry entry)
    {
        ScanForPatchAsync(entry);
        try
        {
            // 에픽게임즈로 설치된 게임은 폴더의 exe를 직접 실행하면 동작하지 않으므로
            // com.epicgames.launcher:// URI로 실행한다. 에픽 게임이 아니면 null → 일반 실행.
            string? epicUri = EpicLauncher.ResolveLaunchUri(entry.GameFolder, entry.ExePath);

            var psi = epicUri is not null
                ? new ProcessStartInfo { FileName = epicUri, UseShellExecute = true }
                : new ProcessStartInfo
                {
                    FileName = entry.ExePath,
                    UseShellExecute = true,
                    WorkingDirectory = entry.GameFolder ?? Path.GetDirectoryName(entry.ExePath) ?? ""
                };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"게임을 실행하지 못했습니다:\n{entry.Name}\n\n{ex.Message}",
                "iPlaySpeed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 「다음」: 현재(또는 유일) 실행 게임 완료 → (CloseOnNext) 종료 →
    /// 표시 순서상 <b>첫 미완료</b> 게임 실행(현재 플레이 중인 순번과 무관).
    /// </summary>
    public void NextGame()
    {
        if (_restartFromTop)
        {
            _restartFromTop = false;
            AdvanceToFirstIncompleteAndLaunch();
            return;
        }

        var running = GetRunningRows();
        // #region agent log
        DebugLog.Write("DailyRunEngine.NextGame", "Next pressed", new
        {
            runningCount = running.Count,
            runningNames = running.Select(r => r.Name).ToArray(),
            lastGame = _lastGameRow?.Name
        }, "H3");
        // #endregion

        GameRow? target = (_lastGameRow is not null && IsRealGameRunning(_lastGameRow.Entry))
            ? _lastGameRow
            : running.FirstOrDefault() ?? _lastGameRow;

        if (target is not null && !target.IsCompleted)
            MarkComplete(target);

        if (_settings.CloseOnNext && target is not null && IsRealGameRunning(target.Entry))
            CloseGame(target.Entry, forceKill: true, skipExitComplete: true);

        AdvanceToFirstIncompleteAndLaunch();
    }

    /// <summary>Rows 맨 위부터 첫 미완료로 포인터 이동 후, 실행 중이 아니면 launch.</summary>
    private void AdvanceToFirstIncompleteAndLaunch()
    {
        var next = FindNextToPlay(-1, null);
        _lastGameRow = next;
        if (next is not null && !IsRealGameRunning(next.Entry))
            LaunchGame(next.Entry);
        NotifyProgressChanged();
    }

    /// <summary>오버레이 아이콘 클릭 — 실행만(다른 게임 종료·완료 없음).</summary>
    public void PlayGame(GameRow row)
    {
        LaunchGame(row.Entry);
        _lastGameRow = row;
        _restartFromTop = false;
        _wasRunning[row.Entry.Id] = true;
    }

    private void MarkComplete(GameRow row)
    {
        if (row.State.ManualOverride == true)
            return;
        row.State.ManualOverride = true;
        row.Refresh(ActionBased);
        NotifyProgressChanged();
        SaveStates();
        // #region agent log
        DebugLog.Write("DailyRunEngine.MarkComplete", "Marked complete", new { game = row.Name }, "H3");
        // #endregion
    }

    private void NotifyProgressChanged()
    {
        var pairs = Rows.Select(r => (r.State, r.Entry.ThresholdMinutes)).ToList();
        ProgressPercent = CompletionJudge.ProgressPercent(pairs, ActionBased);
        OnChanged(nameof(ProgressText));
        OnChanged(nameof(CompletedCount));
        OnChanged(nameof(NextWaitingGameName));
    }

    /// <summary>현재 실제 게임 exe가 실행 중인 Rows.</summary>
    private List<GameRow> GetRunningRows()
    {
        var paths = _cachedRunningPaths ?? SnapshotProcessPaths();
        return Rows.Where(r => IsRealGameRunning(r.Entry, paths)).ToList();
    }

    /// <summary>
    /// 완료 처리 ↔ 되돌리기 토글(오버레이 아이콘 우클릭용). 한 번 누르면 수동 완료,
    /// 다시 누르면 자동 판정으로 되돌린다(실수 클릭 복구용).
    /// </summary>
    public void ToggleComplete(GameRow row)
    {
        row.State.ManualOverride = row.State.ManualOverride == true ? null : true;
        row.Refresh(ActionBased);
        NotifyProgressChanged();
        SaveStates();
    }

    // ── 게임 프로세스 식별(런처/헬퍼 제외) ──

    /// <summary>게임 설치 폴더의 정규화 경로(끝에 구분자). 없으면 null.</summary>
    private static string? GameFolderNorm(GameEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.GameFolder)) return null;
        try { return Path.GetFullPath(entry.GameFolder!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; }
        catch { return null; }
    }

    /// <summary>
    /// 폴더 안의 이 실행파일이 '실제 게임 exe'인지(런처 자체·업데이터·헬퍼는 제외).
    /// 런처 방식 게임은 런처/업데이터가 설치 폴더 '루트'에 있고 실제 게임은 하위 폴더(games\...)에
    /// 있으므로, 루트에 있는 exe는 게임이 아니라고 본다(키워드로 못 거른 런처까지 확실히 제외).
    /// </summary>
    private static bool IsRealGameExe(GameEntry entry, string fullPath)
    {
        // 키워드(런처/업데이터/헬퍼) 제외 — 직접 실행(none) 게임 포함 공통.
        if (LauncherFilter.IsLauncherOrHelper(Path.GetFileNameWithoutExtension(fullPath)))
            return false;

        bool isLauncherType = !string.Equals(entry.LauncherType, "none", StringComparison.OrdinalIgnoreCase);
        if (!isLauncherType)
            return true; // 직접 실행 게임: ExePath 자체가 게임

        // 등록된 런처 exe 자체 제외
        if (!string.IsNullOrWhiteSpace(entry.ExePath))
        {
            try
            {
                if (string.Equals(Path.GetFullPath(entry.ExePath), fullPath, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch { /* 무시 */ }
        }

        // 설치 폴더 '루트'에 있는 exe는 런처/업데이터로 간주(실제 게임은 하위 폴더에 있음)
        string? folder = GameFolderNorm(entry); // 끝에 구분자 포함
        if (folder is not null)
        {
            try
            {
                string? dir = Path.GetDirectoryName(fullPath);
                if (dir is not null)
                {
                    string dirNorm = dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (string.Equals(dirNorm, folder, StringComparison.OrdinalIgnoreCase))
                        return false; // 루트 = 런처/업데이터
                }
            }
            catch { /* 무시 */ }
        }
        return true;
    }

    /// <summary>이번 틱의 전체 프로세스 실행파일 경로 스냅샷(권한 높은 프로세스 포함).</summary>
    private static List<string> SnapshotProcessPaths()
    {
        var paths = new List<string>();
        Process[] all;
        try { all = Process.GetProcesses(); } catch { return paths; }
        try
        {
            foreach (var p in all)
            {
                try
                {
                    string? f = NativeMethods.GetProcessImagePath((uint)p.Id);
                    if (f is null) { try { f = p.MainModule?.FileName; } catch { /* 보호된 프로세스 */ } }
                    if (!string.IsNullOrEmpty(f)) paths.Add(f!);
                }
                catch { /* 무시 */ }
            }
        }
        finally { foreach (var p in all) { try { p.Dispose(); } catch { } } }
        return paths;
    }

    /// <summary>실제 게임 exe(런처 제외)가 실행 중인지 — 미리 만든 경로 스냅샷으로 판정.</summary>
    private static bool IsRealGameRunning(GameEntry entry, List<string> runningPaths)
    {
        string? folder = GameFolderNorm(entry);
        if (folder is not null)
        {
            foreach (var f in runningPaths)
            {
                string full;
                try { full = Path.GetFullPath(f); } catch { continue; }
                if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && IsRealGameExe(entry, full))
                    return true;
            }
        }

        foreach (var f in runningPaths)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (LauncherFilter.IsLauncherOrHelper(name))
                    continue;
                if (ProcessMatcher.MatchesExecutablePath(f, entry.EffectiveGameProcessName(), entry.AllProcessHints()))
                    return true;
            }
            catch { /* 무시 */ }
        }

        if (string.Equals(entry.LauncherType, "none", StringComparison.OrdinalIgnoreCase))
        {
            string pname = entry.EffectiveGameProcessName();
            if (!string.IsNullOrWhiteSpace(pname))
                foreach (var f in runningPaths)
                {
                    try { if (string.Equals(Path.GetFileNameWithoutExtension(f), pname, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch { /* 무시 */ }
                }
        }
        return false;
    }

    /// <summary>실제 게임 exe(런처 제외)가 실행 중인지(스냅샷을 내부에서 1회 생성).</summary>
    private static bool IsRealGameRunning(GameEntry entry) => IsRealGameRunning(entry, SnapshotProcessPaths());

    /// <summary>
    /// startIdx 다음부터(음수면 맨 위부터) 첫 '미완료' 게임을 찾는다.
    /// 끝까지 가면 처음으로 순환한다. exclude(방금 닫은 게임)는 건너뛴다.
    /// </summary>
    private GameRow? FindNextToPlay(int startIdx, GameRow? exclude)
    {
        int count = Rows.Count;
        if (count == 0) return null;
        for (int step = 1; step <= count; step++)
        {
            int idx = startIdx < 0 ? (step - 1) : ((startIdx + step) % count);
            var r = Rows[idx];
            if (r == exclude) continue;
            if (!r.IsCompleted) return r;
        }
        return null;
    }

    /// <summary>진행도를 즉시 0%로 강제 초기화(오늘 분 기준 새 런 시작).</summary>
    public void ForceResetProgress()
    {
        ResetDay(recordPlay: false); // 수동 초기화는 부분 플레이라 학습 기록에 넣지 않음
        _lastGameRow = null;
        _restartFromTop = true;
        _wasRunning.Clear();
        NotifyProgressChanged();
    }

    /// <summary>
    /// foreground 창이 이 게임(런처가 아닌 '실제 게임 exe')인지 판정.
    /// (1) foreground 실행파일이 설치 폴더 안에 있고 런처/업데이터/헬퍼가 아니면 일치.
    /// (2) 폴더가 없거나 경로를 못 읽는 직접 실행(none) 게임은 프로세스 이름으로 일치.
    /// → 런처(launcher_epic 등)가 포그라운드인 동안에는 시간이 누적되지 않고, 실제 게임이 떠야 누적된다.
    /// </summary>
    private static bool MatchesForeground(GameEntry entry, string fgName, string? fgPath)
    {
        string? folder = GameFolderNorm(entry);
        if (folder is not null && !string.IsNullOrEmpty(fgPath))
        {
            try
            {
                string full = Path.GetFullPath(fgPath!);
                if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    bool pathMatch = IsRealGameExe(entry, full);
                    // #region agent log
                    if (!pathMatch && (entry.Name.Contains("NIKKE", StringComparison.OrdinalIgnoreCase)
                        || entry.ProcessHints.Any(h => h.Contains("nikke", StringComparison.OrdinalIgnoreCase))))
                    {
                        DebugLog.Write("DailyRunEngine.MatchesForeground", "Path in folder but not real game exe", new
                        {
                            game = entry.Name,
                            fgName,
                            fgPath = full,
                            launcherType = entry.LauncherType
                        }, "H2");
                    }
                    // #endregion
                    if (pathMatch) return true;
                }
            }
            catch { /* 경로 비교 실패 무시 */ }
        }
        else if (folder is not null && string.IsNullOrEmpty(fgPath)
                 && (entry.Name.Contains("NIKKE", StringComparison.OrdinalIgnoreCase)
                     || entry.ProcessHints.Any(h => h.Contains("nikke", StringComparison.OrdinalIgnoreCase))))
        {
            // #region agent log
            DebugLog.Write("DailyRunEngine.MatchesForeground", "fgPath null", new { game = entry.Name, fgName }, "H2");
            // #endregion
        }
        if (string.Equals(entry.LauncherType, "none", StringComparison.OrdinalIgnoreCase))
        {
            string pname = entry.EffectiveGameProcessName();
            if (ProcessMatcher.MatchesForegroundName(fgName, pname, entry.ProcessHints))
                return true;
        }
        else if (!LauncherFilter.IsLauncherOrHelper(fgName)
                 && ProcessMatcher.MatchesForegroundName(fgName, null, entry.AllProcessHints()))
            return true;
        return false;
    }

    /// <summary>
    /// 게임을 닫는다. forceKill=false면 정상 종료 시도 후 5초 뒤에도 살아있으면 강제 종료(저장 보호),
    /// forceKill=true면 즉시 강제 종료(라이브 서비스 게임용 옵션).
    /// </summary>
    public void CloseGame(GameEntry entry, bool forceKill = false, bool skipExitComplete = false)
    {
        if (skipExitComplete)
            _skipExitComplete.Add(entry.Id);

        var targets = new Dictionary<int, Process>();

        // (1) 등록된 프로세스 이름으로 찾기
        try
        {
            foreach (var p in Process.GetProcessesByName(entry.EffectiveGameProcessName()))
                targets[p.Id] = p;
        }
        catch { /* 무시 */ }

        // (2) 게임 설치 폴더 안에서 실행된 프로세스로 찾기(런처로 켠 실제 게임까지 닫기).
        //     권한 높은 게임 경로도 읽도록 QueryFullProcessImageName 사용.
        string? folder = GameFolderNorm(entry);
        if (folder is not null)
        {
            Process[] all;
            try { all = Process.GetProcesses(); } catch { all = Array.Empty<Process>(); }
            foreach (var p in all)
            {
                bool keep = false;
                try
                {
                    string? f = NativeMethods.GetProcessImagePath((uint)p.Id);
                    if (f is null) { try { f = p.MainModule?.FileName; } catch { /* 보호된 프로세스 */ } }
                    if (f is not null && Path.GetFullPath(f).StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                        keep = true;
                }
                catch { /* 권한/비트수 차이로 접근 불가 */ }

                if (keep) targets[p.Id] = p;
                else if (!targets.ContainsKey(p.Id)) p.Dispose();
            }
        }

        var list = targets.Values.ToList();

        if (forceKill)
        {
            foreach (var p in list)
            {
                try { if (!p.HasExited) p.Kill(); } catch { /* 무시 */ }
                finally { p.Dispose(); }
            }
            return;
        }

        // 정상 종료 시도(저장 보호)
        foreach (var p in list)
        {
            try { p.CloseMainWindow(); } catch { /* 무시 */ }
        }

        // 5초 뒤에도 살아있으면 강제 종료
        Task.Run(async () =>
        {
            await Task.Delay(5000);
            foreach (var p in list)
            {
                try { if (!p.HasExited) p.Kill(); } catch { /* 무시 */ }
                finally { p.Dispose(); }
            }
        });
    }

    /// <summary>특정 게임의 완료/미완료를 수동으로 강제(오판정 대비).</summary>
    public void ToggleManual(GameRow row)
    {
        row.State.ManualOverride = row.State.ManualOverride switch
        {
            null => true,
            true => false,
            false => null
        };
        row.Refresh(ActionBased);
        SaveStates();
    }

    // ── 패치 감지 ──
    private void ScanForPatchAsync(GameEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.GameFolder))
            return;
        var patch = GetOrCreatePatch(entry.Id);
        Task.Run(() =>
        {
            var (totalBytes, lastWrite) = FolderScanner.Scan(entry.GameFolder);
            if (totalBytes <= 0)
                return;

            bool isPatch = PatchPredictor.DetectPatchEvent(patch.LastTotalBytes, totalBytes);
            patch.LastTotalBytes = totalBytes;
            if (lastWrite is DateOnly w)
                patch.LastFolderWrite = w;

            if (isPatch)
            {
                var today = DateOnly.FromDateTime(DateTime.Now);
                if (!patch.ObservedPatchDates.Contains(today))
                    patch.ObservedPatchDates.Add(today);
                if (_states.TryGetValue(entry.Id, out var st))
                    st.UpdateDetectedToday = true;
            }
            SavePatches();
        });
    }

    // ── 리셋/기록/저장 ──
    private void ResetDay(bool recordPlay = true)
    {
        if (recordPlay)
            RecordDailyPlayAndLearn(); // 0으로 지우기 전에 어제 플레이 시간을 학습 기록에 저장

        foreach (var row in Rows)
        {
            row.State.ActivePlaySeconds = 0;
            row.State.UpdateDetectedToday = false;
            row.State.ManualOverride = null;
            row.Refresh(ActionBased);
        }
        _lastGameRow = null;
        _wasRunning.Clear();
        _settings.LastReset = DateTime.Now;
        _dayRecorded = false;
        _store.Save(AppPaths.SettingsFile, _settings);
        SaveStates();
    }

    private void RecordDayComplete()
    {
        _dayRecorded = true;
        var history = _store.Load(AppPaths.HistoryFile, new List<DailyRecord>());
        var today = DateOnly.FromDateTime(DateTime.Now);
        int totalMinutes = Rows.Sum(r => r.State.ActivePlaySeconds) / 60;
        history.RemoveAll(h => h.Date == today);
        history.Add(new DailyRecord
        {
            Date = today,
            CompletedGames = CompletedCount,
            TotalGames = Rows.Count,
            TotalMinutes = totalMinutes,
            EndTime = TimeOnly.FromDateTime(DateTime.Now)
        });
        _store.Save(AppPaths.HistoryFile, history);
    }

    private void SaveStates() => _store.Save(AppPaths.StatesFile, _states);

    private void SavePatches()
    {
        List<GamePatchInfo> snapshot;
        lock (_patchLock)
            snapshot = _patches.Values.ToList();
        _store.Save(AppPaths.PatchesFile, snapshot);
    }

    private GameDailyState GetOrCreateState(string id)
    {
        if (!_states.TryGetValue(id, out var s))
        {
            s = new GameDailyState();
            _states[id] = s;
        }
        return s;
    }

    private GamePatchInfo GetOrCreatePatch(string id)
    {
        lock (_patchLock)
        {
            if (!_patches.TryGetValue(id, out var p))
            {
                p = new GamePatchInfo { GameId = id };
                _patches[id] = p;
            }
            return p;
        }
    }

    private static Dictionary<string, GamePatchInfo> LoadPatches(JsonStore store)
    {
        var list = store.Load(AppPaths.PatchesFile, new List<GamePatchInfo>());
        return list.Where(p => !string.IsNullOrEmpty(p.GameId))
                   .GroupBy(p => p.GameId)
                   .ToDictionary(g => g.Key, g => g.First());
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        SaveStates();
        SavePatches();
        _store.Save(AppPaths.SettingsFile, _settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
