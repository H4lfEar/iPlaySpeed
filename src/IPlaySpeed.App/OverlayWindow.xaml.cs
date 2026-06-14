using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using IPlaySpeed.App.Native;
using IPlaySpeed.App.Services;
using IPlaySpeed.App.ViewModels;
using IPlaySpeed.Core;

namespace IPlaySpeed.App;

public partial class OverlayWindow : Window
{
    private readonly DailyRunEngine _engine;
    private readonly AppSettings _settings;
    private readonly JsonStore _store;
    private readonly MediaController _media;
    private readonly VolumeController _volume;
    private readonly DispatcherTimer _timer;

    private bool _minimized;
    private string _lastMediaTitle = ""; // 첫 갱신을 강제하기 위한 더미
    private string _lastSourceId = "";

    // 볼륨 슬라이더 동기화 플래그: 프로그램이 슬라이더 값을 맞추는 동안 사용자 ValueChanged와 구분.
    // 필드 초기화는 InitializeComponent보다 먼저 실행되므로 XAML 초기화 중 ValueChanged를 무시한다.
    // 생성자 끝에서 false로 푼다.
    private bool _updatingVol = true;
    private string _lastGameVolId = " ";   // 직전 게임 식별자(바뀔 때만 실제 볼륨 재조회)
    private string _lastMediaVolId = " ";  // 직전 미디어 프로세스명

    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x36));
    private static readonly Brush FillBrush = new SolidColorBrush(Color.FromRgb(0x39, 0xD9, 0x8A));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF));
    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x57, 0x63));

    public OverlayWindow(DailyRunEngine engine, AppSettings settings, JsonStore store, MediaController media, VolumeController volume)
    {
        InitializeComponent();
        _engine = engine;
        _settings = settings;
        _store = store;
        _media = media;
        _volume = volume;

        Left = _settings.OverlayLeft;
        Top = _settings.OverlayTop;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => { Render(); RefreshMedia(); };

        _engine.PropertyChanged += OnEnginePropertyChanged;
        _engine.VisualStateChanged += Render;

        _updatingVol = false; // 이후 슬라이더 변경은 사용자 조작으로 간주

        Loaded += (_, _) => { Render(); RefreshMedia(); };
        Closed += (_, _) => OnClosedCleanup();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Render(); _timer.Start(); } else _timer.Stop(); };
    }

    // ── 축소/복원 ──
    private void OnToggleMinimize(object sender, RoutedEventArgs e) => SetMinimized(!_minimized);

    private void SetMinimized(bool m)
    {
        _minimized = m;
        GaugeCanvas.Visibility = m ? Visibility.Collapsed : Visibility.Visible;
        MediaPanel.Visibility = m ? Visibility.Collapsed : Visibility.Visible;
        VolumePanel.Visibility = m ? Visibility.Collapsed : Visibility.Visible;
        BtnMin.Content = m ? "❐" : "—";
        BtnMin.ToolTip = m ? "원래 크기로" : "축소";
        Render();
    }

    // ── 게이지 렌더 ──
    public void Render()
    {
        TxtProgress.Text = $"{_engine.ProgressPercent:0}%";
        // 현재(또는 백그라운드 카운트 중인) 게임 이름 + 누적 플레이 시간(분:초)을 매초 갱신해 표시.
        TxtCurrent.Text = _minimized
            ? $"다음: {_engine.NextWaitingGameName}"
            : _engine.HasDisplayGame
                ? $"▶ {_engine.DisplayGameName}  {Fmt(TimeSpan.FromSeconds(_engine.DisplayPlaySeconds))}"
                : _engine.DisplayGameName;

        if (_minimized) return; // 축소 상태면 게이지/미디어는 숨김

        var canvas = GaugeCanvas;
        canvas.Children.Clear();

        double w = canvas.Width;
        double h = canvas.Height;
        double trackY = h - 14;
        double trackH = 8;

        canvas.Children.Add(Place(new Rectangle { Width = w, Height = trackH, RadiusX = 4, RadiusY = 4, Fill = TrackBrush }, 0, trackY));

        double fillW = Math.Max(0, Math.Min(1, _engine.ProgressPercent / 100.0)) * w;
        if (fillW > 0)
            canvas.Children.Add(Place(new Rectangle { Width = fillW, Height = trackH, RadiusX = 4, RadiusY = 4, Fill = FillBrush }, 0, trackY));

        var rows = _engine.Rows;
        int n = rows.Count;
        const double size = 28;
        for (int i = 0; i < n; i++)
        {
            GameRow row = rows[i];
            double cx = w * (i + 0.5) / n;
            double iconLeft = cx - size / 2;

            Brush ring = row.IsCurrent ? AccentBrush : row.IsCompleted ? FillBrush : DimBrush;
            double fill = CompletionJudge.ProgressRatio(row.State, row.Entry.ThresholdMinutes);

            // 클릭 가능한 아이콘 박스(링+원형크롭 아이콘+차오름). 클릭 시 해당 게임 실행 + 살짝 확대.
            var box = BuildIconBox(row, size, ring, fill);
            canvas.Children.Add(Place(box, iconLeft - 3, -3));

            if (row.IsCompleted)
                canvas.Children.Add(Place(new TextBlock { Text = "✓", Foreground = FillBrush, FontWeight = FontWeights.Bold, FontSize = 12 }, cx + 6, trackY - 18));
        }
    }

    /// <summary>
    /// 게이지의 게임 아이콘 1개를 만든다: 원형 링 + 원형으로 크롭된 아이콘(어둡게) + 완료비율만큼
    /// 아래에서 차오르는 밝은 아이콘. 클릭하면 해당 게임을 실행하고 살짝 커지는 애니메이션을 준다.
    /// </summary>
    private FrameworkElement BuildIconBox(GameRow row, double size, Brush ring, double fill)
    {
        double boxSize = size + 6;
        var scale = new ScaleTransform(1, 1);
        var box = new Grid
        {
            Width = boxSize,
            Height = boxSize,
            Background = Brushes.Transparent, // 빈 곳도 클릭 받도록
            Cursor = Cursors.Hand,
            Tag = row,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            ToolTip = $"{row.Name}  ·  좌클릭: 실행 / 우클릭: 완료 처리"
        };

        // 원형 링
        box.Children.Add(new Ellipse { Width = boxSize, Height = boxSize, Stroke = ring, StrokeThickness = 2, Fill = Brushes.Transparent });

        var center = new Point(size / 2, size / 2);
        var circle = new EllipseGeometry(center, size / 2, size / 2);

        if (row.Icon is not null)
        {
            box.Children.Add(new Image
            {
                Source = row.Icon, Width = size, Height = size, Opacity = 0.28,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Clip = circle
            });
            if (fill > 0.001)
            {
                // 원형 ∩ (아래에서 차오른 사각형) 으로 크롭
                var rect = new RectangleGeometry(new Rect(0, size * (1 - fill), size, size * fill));
                box.Children.Add(new Image
                {
                    Source = row.Icon, Width = size, Height = size, Opacity = 1.0,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Clip = new CombinedGeometry(GeometryCombineMode.Intersect, circle, rect)
                });
            }
        }
        else
        {
            box.Children.Add(new Ellipse { Width = size, Height = size, Fill = DimBrush });
            if (fill > 0.001)
            {
                var rect = new RectangleGeometry(new Rect(0, size * (1 - fill), size, size * fill));
                box.Children.Add(new Ellipse
                {
                    Width = size, Height = size, Fill = ring,
                    Clip = new CombinedGeometry(GeometryCombineMode.Intersect, circle, rect)
                });
            }
        }

        box.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // 오버레이 드래그 방지
            var a = new DoubleAnimation { To = 1.18, Duration = TimeSpan.FromMilliseconds(90), AutoReverse = true };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        };
        box.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (box.Tag is GameRow r) _engine.PlayGame(r);
        };
        box.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (box.Tag is GameRow r) { _engine.ToggleComplete(r); Render(); }
        };
        return box;
    }

    private static T Place<T>(T element, double left, double top) where T : UIElement
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return element;
    }

    // ── 미디어 ──
    private async void RefreshMedia()
    {
        await _media.RefreshAsync();

        if (_minimized) return; // 축소 중엔 미디어 UI 갱신 불필요

        // 재생 시간
        TimeSpan pos = _media.Position, dur = _media.Duration;
        TxtTime.Text = dur > TimeSpan.Zero ? $"{Fmt(pos)} / {Fmt(dur)}"
                     : pos > TimeSpan.Zero ? Fmt(pos) : "";

        // 출처 아이콘 (앱이 바뀔 때만 갱신)
        if (_media.SourceAppId != _lastSourceId)
        {
            _lastSourceId = _media.SourceAppId;
            UpdateSourceIcon(_media.SourceAppId);
        }

        // 제목 (바뀔 때만 마퀴 재설정)
        string t = _media.CurrentTitle;
        if (t != _lastMediaTitle)
        {
            _lastMediaTitle = t;
            UpdateMarquee(); // 텍스트 설정 + (길면) 끊김 없는 루프 스크롤
        }

        RefreshVolume();
    }

    // ── 볼륨 분리(게임/미디어) ──

    /// <summary>
    /// 현재 게임/미디어 출처가 '바뀐 경우에만' 실제 시스템 볼륨을 읽어 슬라이더에 반영한다.
    /// 매초 덮어쓰지 않으므로 사용자가 슬라이더를 드래그하는 동안 값이 튀지 않는다.
    /// </summary>
    private void RefreshVolume()
    {
        if (_minimized) return;

        // 대상이 바뀌면 실제 볼륨을 다시 읽는다. 또한 대상이 있는데 아직 비활성(세션 미발견)이면
        // 매 틱 재시도해, 게임/미디어가 소리를 내기 시작하면 슬라이더가 자동 활성화되게 한다.
        // (활성화된 뒤에는 다시 읽지 않아 드래그 중 값이 튀지 않는다.)
        GameEntry? entry = _engine.CurrentEntry;
        string gameId = entry?.Id ?? "";
        if (gameId != _lastGameVolId || (entry is not null && !GameVolume.IsEnabled))
        {
            _lastGameVolId = gameId;
            float? v = entry is not null ? _volume.GetGameVolume(entry) : null;
            ApplySliderValue(GameVolume, TxtGameVol, v);
        }

        string mediaProc = MediaController.ResolveSourceProcessName(_media.SourceAppId) ?? "";
        if (mediaProc != _lastMediaVolId || (mediaProc.Length > 0 && !MediaVolume.IsEnabled))
        {
            _lastMediaVolId = mediaProc;
            float? v = mediaProc.Length > 0 ? _volume.GetMediaVolume(mediaProc) : null;
            ApplySliderValue(MediaVolume, TxtMediaVol, v);
        }
    }

    /// <summary>슬라이더 값을 프로그램적으로 설정(사용자 이벤트와 구분하기 위해 플래그로 감싼다). null이면 비활성.</summary>
    private void ApplySliderValue(Slider slider, TextBlock label, float? vol)
    {
        _updatingVol = true;
        try
        {
            if (vol is float v)
            {
                slider.IsEnabled = true;
                slider.Value = Math.Round(v * 100);
                label.Text = $"{slider.Value:0}";
            }
            else
            {
                slider.IsEnabled = false;
                slider.Value = 0;
                label.Text = "--";
            }
        }
        finally { _updatingVol = false; }
    }

    private void OnGameVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVol) return;
        GameEntry? entry = _engine.CurrentEntry;
        if (entry is null) return;
        _volume.SetGameVolume(entry, (float)(e.NewValue / 100.0));
        TxtGameVol.Text = $"{e.NewValue:0}";
    }

    private void OnMediaVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVol) return;
        string mediaProc = MediaController.ResolveSourceProcessName(_media.SourceAppId) ?? "";
        if (mediaProc.Length == 0) return;
        _volume.SetMediaVolume(mediaProc, (float)(e.NewValue / 100.0));
        TxtMediaVol.Text = $"{e.NewValue:0}";
    }

    private void UpdateSourceIcon(string appId)
    {
        try
        {
            string? exe = MediaController.ResolveSourceExe(appId);
            if (exe is null) { SrcIcon.Source = null; return; }

            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "iPlaySpeed_src_" + System.IO.Path.GetFileNameWithoutExtension(exe) + ".png");
            string? png = IconExtractor.ExtractToPng(exe, tmp);
            SrcIcon.Source = IconExtractor.LoadImage(png);
        }
        catch { SrcIcon.Source = null; }
    }

    /// <summary>
    /// 제목이 고정 구간(150px)보다 길면 좌측으로 끊김 없이 순환 스크롤(마퀴), 아니면 정지.
    /// 제목을 [제목][간격][제목] 으로 두 번 이어 붙이고 '제목+간격' 만큼만 이동시키면,
    /// 끝 위치에서 두 번째 제목이 처음 위치와 정확히 겹쳐 이음매 없이 반복된다.
    /// </summary>
    private void UpdateMarquee()
    {
        MarqueeTransform.BeginAnimation(TranslateTransform.XProperty, null);
        MarqueeTransform.X = 0;

        string title = _lastMediaTitle;
        if (string.IsNullOrWhiteSpace(title))
        {
            TxtMedia.Text = "재생 중인 미디어 없음";
            TxtMedia2.Visibility = Visibility.Collapsed;
            return;
        }

        // FormattedText로 레이아웃 상태와 무관하게 정확한 폭을 측정한다.
        double clipW = MarqueeClip.Width > 0 ? MarqueeClip.Width : 150;
        double wSingle = MeasureTextWidth(title);

        if (wSingle <= clipW)
        {
            TxtMedia.Text = title;                 // 짧으면 스크롤 없이 그대로
            TxtMedia2.Visibility = Visibility.Collapsed;
            return;
        }

        // 두 번째 제목을 정확히 (제목폭 + 간격) 위치에 두고 그만큼만 이동 → 좌표와 이동량이 같아
        // 끝점에서 두 번째 제목이 처음 위치와 정확히 겹친다(빈칸 없이 매끄러운 순환).
        const double gap = 36; // 두 반복 사이 간격(px)
        double unit = wSingle + gap;

        TxtMedia.Text = title;
        TxtMedia2.Text = title;
        Canvas.SetLeft(TxtMedia2, unit);
        TxtMedia2.Visibility = Visibility.Visible;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = -unit,
            Duration = TimeSpan.FromSeconds(Math.Max(1.5, unit / 60.0)), // 약 60px/초(이전보다 빠르게)
            RepeatBehavior = RepeatBehavior.Forever
        };
        MarqueeTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <summary>TxtMedia의 폰트로 주어진 문자열의 렌더 폭(px)을 계산한다.</summary>
    private double MeasureTextWidth(string s)
    {
        double pixelsPerDip = 1.0;
        try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { /* 기본 1.0 */ }
        var ft = new FormattedText(
            s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(TxtMedia.FontFamily, TxtMedia.FontStyle, TxtMedia.FontWeight, TxtMedia.FontStretch),
            TxtMedia.FontSize, Brushes.White, pixelsPerDip);
        return ft.WidthIncludingTrailingWhitespace;
    }

    private void OnEnginePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DailyRunEngine.ProgressPercent)
            or nameof(DailyRunEngine.ProgressText)
            or nameof(DailyRunEngine.DisplayGameName)
            or nameof(DailyRunEngine.DisplayPlaySeconds)
            or nameof(DailyRunEngine.CurrentGameName))
            Render();
    }

    private void OnClosedCleanup()
    {
        _engine.PropertyChanged -= OnEnginePropertyChanged;
        _engine.VisualStateChanged -= Render;
    }

    private static string Fmt(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    private void OnMediaPrev(object sender, RoutedEventArgs e) => _media.Previous();
    private void OnMediaPlayPause(object sender, RoutedEventArgs e) => _media.PlayPause();
    private void OnMediaNext(object sender, RoutedEventArgs e) => _media.Next();

    // ── 게임 컨트롤 ──
    private void OnNext(object sender, RoutedEventArgs e) => _engine.NextGame();
    private void OnHide(object sender, RoutedEventArgs e) => Hide();

    // ── 드래그 이동 ──
    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button) return;
        try { DragMove(); } catch { /* 무시 */ }
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        _settings.OverlayLeft = Left;
        _settings.OverlayTop = Top;
        _store.Save(AppPaths.SettingsFile, _settings);
    }
}
