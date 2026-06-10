using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IPlaySpeed.App.Services;

/// <summary>오버레이 조작법을 안내하는 이미지를 코드로 그려서 만든다(설정창 도움말용).</summary>
public static class OverlayHelpImage
{
    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x26));
    private static readonly Brush Card = new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x36));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF2));
    private static readonly Brush Sub = new SolidColorBrush(Color.FromRgb(0x9A, 0x9C, 0xA8));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x4F, 0x8C, 0xFF));
    private static readonly Brush Done = new SolidColorBrush(Color.FromRgb(0x39, 0xD9, 0x8A));

    private static readonly (string key, string desc)[] Items =
    {
        ("게임 아이콘 좌클릭", "해당 게임 실행 (설정에 따라 현재 게임 종료/유지)"),
        ("게임 아이콘 우클릭", "해당 게임을 즉시 '완료' 처리"),
        ("▶ 다음", "현재 게임을 종료/유지 후 다음 미완료 게임 실행"),
        ("— / ❐", "오버레이 축소 / 복원"),
        ("✕", "오버레이 숨김 (단축키로 다시 표시)"),
        ("상단 바 드래그", "오버레이 위치 이동"),
        ("⏮ ⏯ ⏭", "미디어 이전 / 재생·정지 / 다음"),
        ("🎮 / 🔊 슬라이더", "게임 볼륨 / 미디어 볼륨 개별 조절"),
        ("아이콘 차오름", "완료 시간까지 진행될수록 아이콘이 차오르며 밝아짐"),
    };

    public static ImageSource Generate()
    {
        const double W = 560;
        double top = 58;
        double rowH = 32;
        double H = top + Items.Length * rowH + 24;

        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Bg, null, new Rect(0, 0, W, H));
            dc.DrawText(FT("오버레이 조작법", 20, Text, bold: true), new Point(22, 18));

            double y = top;
            foreach (var (key, desc) in Items)
            {
                dc.DrawRoundedRectangle(Card, null, new Rect(16, y, W - 32, rowH - 8), 8, 8);
                dc.DrawText(FT(key, 13, Accent, bold: true), new Point(28, y + 5));
                dc.DrawText(FT(desc, 12.5, Sub), new Point(210, y + 6));
                y += rowH;
            }

            dc.DrawText(FT("전역 단축키: 오버레이 토글 / 다음 게임 (설정에서 변경)", 11.5, Done),
                new Point(22, H - 22));
        }

        var rtb = new RenderTargetBitmap((int)W, (int)H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static FormattedText FT(string s, double size, Brush brush, bool bold = false) =>
        new(
            s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI, Malgun Gothic"),
                FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size, brush, 1.0);
}
