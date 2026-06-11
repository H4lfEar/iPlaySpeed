using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using IPlaySpeed.App.Models;
using IPlaySpeed.App.Native;
using IPlaySpeed.App.Services;
using IPlaySpeed.App.ViewModels;
using IPlaySpeed.Core;

namespace IPlaySpeed.App;

public partial class MainWindow : Window
{
    private const string DragFormat = "iPlaySpeed.GameCandidate";

    private readonly JsonStore _store;
    private readonly AppSettings _settings;
    private readonly GameLibrary _library;
    private readonly DailyRunEngine _engine;
    private readonly MediaController _media = new();
    private readonly VolumeController _volume = new();
    private readonly ObservableCollection<GameCandidate> _detected = new();
    private readonly HashSet<string> _hidden; // 사용자가 감지 목록에서 숨긴(게임 아님) 항목 키

    private GlobalHotkey? _hotkey;
    private OverlayWindow? _overlay;
    private Point _dragStart;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exiting; // 트레이 '종료'로 실제 종료할 때만 true

    public MainWindow()
    {
        InitializeComponent();

        Icon = AppIconFactory.WindowIcon(); // 창/작업표시줄 아이콘
        Closing += OnClosing;               // 닫기(X) 시 종료 대신 트레이로 (최소화는 작업표시줄 유지)

        _store = new JsonStore(AppPaths.DataDir);
        _settings = _store.Load(AppPaths.SettingsFile, new AppSettings());
        _library = new GameLibrary(_store);
        _hidden = new HashSet<string>(
            _store.Load(AppPaths.HiddenFile, new List<string>()), StringComparer.OrdinalIgnoreCase);
        _engine = new DailyRunEngine(_library, _store, _settings);

        DataContext = _engine;
        DetectedList.ItemsSource = _detected;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _engine.Start();

        _hotkey = new GlobalHotkey();
        _hotkey.Register(_settings.OverlayHotkey, ToggleOverlay);
        _hotkey.Register(_settings.NextGameHotkey, () => _engine.NextGame());

        SetupTray();

        await _media.InitAsync();
        await RunDetectAsync();

        _ = UpdateService.CheckAsync(); // 시작 시 백그라운드로 업데이트 확인(설치형일 때만)
    }

    // ── 설치 게임 감지 ──
    private async System.Threading.Tasks.Task RunDetectAsync()
    {
        DetectStatus.Text = "설치된 게임을 검색하는 중...";
        var list = await System.Threading.Tasks.Task.Run(() => GameDetector.DetectAll());

        _detected.Clear();
        int n = 0;
        foreach (var c in list)
        {
            if (IsAlreadyRegistered(c.ExePath))
                continue;
            if (_hidden.Contains(DetectionKey(c)))   // 사용자가 숨긴(게임 아님) 항목 제외
                continue;
            _detected.Add(c);
            n++;
        }
        DetectStatus.Text = n == 0
            ? "감지된 게임이 없습니다. '＋ 직접 추가'로 등록하세요."
            : $"{n}개 감지됨. 오른쪽으로 끌어다 놓거나 '추가 ▶'를 누르세요.";
    }

    private bool IsAlreadyRegistered(string exePath) =>
        !string.IsNullOrEmpty(exePath) &&
        _library.Games.Any(g => string.Equals(g.ExePath, exePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>감지 항목의 숨김 식별 키(실행파일 경로 우선, 없으면 이름).</summary>
    private static string DetectionKey(GameCandidate c) =>
        !string.IsNullOrEmpty(c.ExePath) ? c.ExePath.ToLowerInvariant() : "name:" + c.Name.ToLowerInvariant();

    /// <summary>감지 목록에서 항목을 숨기고(영구) 목록에서 제거. 다음 스캔에도 다시 안 보인다.</summary>
    private void OnHideDetected(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameCandidate c) return;
        _hidden.Add(DetectionKey(c));
        _store.Save(AppPaths.HiddenFile, _hidden.ToList());
        _detected.Remove(c);
    }

    private async void OnRescan(object sender, RoutedEventArgs e) => await RunDetectAsync();

    // ── 후보 → 등록 ──
    private void AddCandidate(GameCandidate candidate)
    {
        var entry = _library.AddFromCandidate(candidate);
        if (!_engine.Rows.Any(r => r.Entry.Id == entry.Id))
            _engine.AddGame(entry);
        _detected.Remove(candidate);
    }

    private void OnAddDetected(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameCandidate c)
            AddCandidate(c);
    }

    // ── 드래그 앤 드롭 (좌 → 우) ──
    private void OnDetectedMouseDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = e.GetPosition(null);

    private void OnDetectedMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is Button) return; // 버튼 클릭은 드래그로 보지 않음

        Point pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if ((e.OriginalSource as FrameworkElement)?.DataContext is GameCandidate c)
        {
            var data = new DataObject(DragFormat, c);
            DragDrop.DoDragDrop(DetectedList, data, DragDropEffects.Copy);
        }
    }

    private void OnDragEnterRegistered(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DragFormat))
        {
            e.Effects = DragDropEffects.Copy;
            RegisteredDropZone.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
            RegisteredDropZone.BorderThickness = new Thickness(2);
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeaveRegistered(object sender, DragEventArgs e) =>
        RegisteredDropZone.BorderThickness = new Thickness(0);

    private void OnDropToRegistered(object sender, DragEventArgs e)
    {
        RegisteredDropZone.BorderThickness = new Thickness(0);
        if (e.Data.GetDataPresent(DragFormat) &&
            e.Data.GetData(DragFormat) is GameCandidate c)
            AddCandidate(c);
    }

    // ── 등록 목록 조작 ──
    private void OnRegisterGame(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "게임 또는 런처 실행 파일 선택",
            Filter = "실행 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var entry = _library.AddFromExe(dlg.FileName);
        if (!_engine.Rows.Any(r => r.Entry.Id == entry.Id))
            _engine.AddGame(entry);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameRow row)
            _engine.MoveUp(row);
    }

    private void OnMoveDown(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameRow row)
            _engine.MoveDown(row);
    }

    private void OnThresholdChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || tb.DataContext is not GameRow row) return;
        if (int.TryParse(tb.Text.Trim(), out int v))
            _engine.SetThreshold(row, v);
        else
            tb.Text = row.ThresholdMinutes.ToString(); // 잘못된 입력 되돌림
    }

    private void OnLaunchRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameRow row)
            _engine.LaunchGame(row.Entry);
    }

    private void OnToggleManualRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameRow row)
            _engine.ToggleManual(row);
    }

    private void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameRow row) return;
        if (MessageBox.Show($"'{row.Name}' 등록을 해제할까요?", "iPlaySpeed",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _library.Remove(row.Entry);
            _engine.RemoveRow(row);
        }
    }

    // ── 시스템 트레이 ──
    private void SetupTray()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("열기", null, (_, _) => ShowFromTray());
        menu.Items.Add("오버레이 표시/숨김", null, (_, _) => ToggleOverlay());
        menu.Items.Add("진행도 초기화", null, (_, _) => _engine.ForceResetProgress());
        menu.Items.Add("업데이트 확인", null, (_, _) => _ = UpdateService.CheckAsync(silentIfNone: false));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => ExitFromTray());

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = AppIconFactory.TrayIcon(),
            Visible = true,
            Text = "iPlaySpeed",
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    // 단일 인스턴스: 다른(비승격) 인스턴스가 보낸 '창 표시' 브로드캐스트를 받기 위한 후킹.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        // 비승격 프로세스가 보낸 등록 메시지가 승격 창에 닿도록 허용.
        NativeMethods.ChangeWindowMessageFilterEx(hwnd, AppInstance.ShowMessage, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == AppInstance.ShowMessage)
        {
            ShowFromTray(); // 트레이/최소화 상태에서 복원 + 활성화
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 닫기(X)는 종료가 아니라 트레이로 보낸다. 실제 종료는 트레이 '종료' 메뉴로만.
        if (_exiting) return;
        e.Cancel = true;
        Hide();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _exiting = true;
        Close();
    }

    // ── 오버레이 / 설정 ──
    private void OnToggleOverlay(object sender, RoutedEventArgs e) => ToggleOverlay();

    private void ToggleOverlay()
    {
        if (_overlay is { IsVisible: true }) { _overlay.Hide(); return; }
        _overlay ??= new OverlayWindow(_engine, _settings, _store, _media, _volume);
        _overlay.Show();
    }

    private void OnNextGame(object sender, RoutedEventArgs e) => _engine.NextGame();

    private void OnResetProgress(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("오늘 진행도를 모두 0%로 초기화할까요?", "iPlaySpeed",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _engine.ForceResetProgress();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings, _store) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _hotkey?.Dispose();
            _hotkey = new GlobalHotkey();
            _hotkey.Register(_settings.OverlayHotkey, ToggleOverlay);
            _hotkey.Register(_settings.NextGameHotkey, () => _engine.NextGame());

            if (_settings.AutoLearnThreshold)
                _engine.ApplyLearnedThresholds(); // 켜는 즉시 기존 기록으로 반영
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotkey?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _overlay?.Close();
        _engine.Dispose();
        _volume.Dispose();
    }
}
