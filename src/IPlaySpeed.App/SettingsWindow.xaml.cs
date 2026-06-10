using System.Windows;
using IPlaySpeed.App.Native;
using IPlaySpeed.App.Services;
using IPlaySpeed.Core;

namespace IPlaySpeed.App;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly JsonStore _store;

    public SettingsWindow(AppSettings settings, JsonStore store)
    {
        InitializeComponent();
        _settings = settings;
        _store = store;

        TxtHour.Text = settings.ResetHour.ToString();
        TxtMinute.Text = settings.ResetMinute.ToString();
        TxtInterval.Text = settings.DefaultPatchIntervalDays.ToString();
        TxtOverlayKey.Text = settings.OverlayHotkey;
        TxtNextKey.Text = settings.NextGameHotkey;
        ChkAutoClick.IsChecked = settings.LauncherAutoClickEnabled;
        ChkCountWhileRunning.IsChecked = settings.CountWhileRunning;
        ChkAutoLearn.IsChecked = settings.AutoLearnThreshold;
        RadCloseOnNext.IsChecked = settings.CloseOnNext;
        RadKeepOnNext.IsChecked = !settings.CloseOnNext;

        ChkStartup.IsChecked = StartupManager.IsEnabled();
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        TxtVersion.Text = ver is null ? "" : $"현재 버전 {ver.Major}.{ver.Minor}.{ver.Build}";
    }

    private void OnCheckUpdate(object sender, RoutedEventArgs e) =>
        _ = UpdateService.CheckAsync(silentIfNone: false);

    private void OnAutoClickChecked(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "런처 자동 클릭을 켭니다.\n\n" +
            "• 게임사 이용약관 위반 및 계정 정지 위험이 있습니다.\n" +
            "• 안티치트가 비정상 동작으로 탐지할 수 있습니다.\n" +
            "• 비밀번호/로그인 자동화는 수행하지 않습니다.\n\n" +
            "위험을 이해했고 본인 책임으로 사용하시겠습니까?",
            "위험 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            ChkAutoClick.IsChecked = false; // 동의하지 않으면 다시 끔
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TryParseRange(TxtHour.Text, 0, 23, out int hour))
        { Warn("시각(시)은 0~23 사이여야 합니다."); return; }
        if (!TryParseRange(TxtMinute.Text, 0, 59, out int minute))
        { Warn("시각(분)은 0~59 사이여야 합니다."); return; }
        if (!TryParseRange(TxtInterval.Text, 1, 365, out int interval))
        { Warn("기본 패치 주기는 1~365 사이여야 합니다."); return; }

        string overlayKey = TxtOverlayKey.Text.Trim();
        string nextKey = TxtNextKey.Text.Trim();
        if (!GlobalHotkey.TryParse(overlayKey, out _, out _))
        { Warn("오버레이 토글 단축키 형식이 올바르지 않습니다."); return; }
        if (!GlobalHotkey.TryParse(nextKey, out _, out _))
        { Warn("다음 게임 단축키 형식이 올바르지 않습니다."); return; }

        _settings.ResetHour = hour;
        _settings.ResetMinute = minute;
        _settings.DefaultPatchIntervalDays = interval;
        _settings.OverlayHotkey = overlayKey;
        _settings.NextGameHotkey = nextKey;
        _settings.LauncherAutoClickEnabled = ChkAutoClick.IsChecked == true;
        _settings.CountWhileRunning = ChkCountWhileRunning.IsChecked == true;
        _settings.AutoLearnThreshold = ChkAutoLearn.IsChecked == true;
        _settings.CloseOnNext = RadCloseOnNext.IsChecked == true;

        _store.Save(AppPaths.SettingsFile, _settings);

        // 시작 프로그램 등록 상태가 바뀌었으면 반영
        bool wantStartup = ChkStartup.IsChecked == true;
        if (wantStartup != StartupManager.IsEnabled())
        {
            if (!StartupManager.SetEnabled(wantStartup))
                MessageBox.Show(
                    "시작 프로그램 설정을 변경하지 못했습니다.\n관리자 권한으로 실행 중인지 확인해 주세요.",
                    "iPlaySpeed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>오버레이 조작법 안내 이미지를 별도 창으로 보여준다.</summary>
    private void OnShowHelp(object sender, RoutedEventArgs e)
    {
        var img = new System.Windows.Controls.Image
        {
            Source = OverlayHelpImage.Generate(),
            Stretch = System.Windows.Media.Stretch.None,
            SnapsToDevicePixels = true
        };
        var win = new Window
        {
            Title = "오버레이 조작법",
            Content = img,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = System.Windows.Media.Brushes.Black
        };
        win.ShowDialog();
    }

    private static bool TryParseRange(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), out value) && value >= min && value <= max;

    private static void Warn(string message) =>
        MessageBox.Show(message, "iPlaySpeed", MessageBoxButton.OK, MessageBoxImage.Warning);
}
