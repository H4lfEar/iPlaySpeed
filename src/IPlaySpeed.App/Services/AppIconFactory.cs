using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace IPlaySpeed.App.Services;

/// <summary>
/// 창/작업표시줄/시스템 트레이 아이콘을 제공한다.
/// 출력 폴더의 assets\app.ico 또는 app.png 가 있으면 그것을 쓰고, 없으면 폴백 아이콘을 그려서 쓴다.
/// (사용자가 로고 파일을 넣으면 자동으로 적용된다.)
/// </summary>
public static class AppIconFactory
{
    private const string PngUri = "pack://application:,,,/assets/app.png";
    private const string IcoUri = "pack://application:,,,/assets/app.ico";
    private static string AssetIco => Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
    private static string AssetPng => Path.Combine(AppContext.BaseDirectory, "assets", "app.png");

    /// <summary>WPF 창/작업표시줄 아이콘. 임베드 리소스 → 외부 파일 → 폴백 순.</summary>
    public static ImageSource WindowIcon()
    {
        // 1) 임베드 리소스(pack URI)
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(PngUri, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { /* 다음 폴백 */ }

        // 2) 외부 파일
        string? file = File.Exists(AssetIco) ? AssetIco : File.Exists(AssetPng) ? AssetPng : null;
        if (file is not null)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(file);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { /* 폴백으로 */ }
        }
        return FallbackImageSource();
    }

    /// <summary>시스템 트레이용 아이콘. 임베드 리소스 → 외부 파일 → 폴백 순.</summary>
    public static Drawing.Icon TrayIcon()
    {
        // 1) 임베드 리소스(pack URI)의 .ico
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(IcoUri, UriKind.Absolute));
            if (info is not null)
            {
                using var s = info.Stream;
                return new Drawing.Icon(s, new Drawing.Size(32, 32));
            }
        }
        catch { /* 다음 폴백 */ }

        // 2) 외부 파일
        try
        {
            if (File.Exists(AssetIco))
                return new Drawing.Icon(AssetIco, new Drawing.Size(32, 32));
            if (File.Exists(AssetPng))
            {
                using var src = new Drawing.Bitmap(AssetPng);
                using var resized = new Drawing.Bitmap(src, new Drawing.Size(32, 32));
                return Drawing.Icon.FromHandle(resized.GetHicon());
            }
        }
        catch { /* 폴백으로 */ }
        return FallbackIcon();
    }

    // ── 폴백: 검은 원 + 번개(흰색) 간단 도형 ──
    private static readonly Drawing.PointF[] Bolt =
    {
        new(19, 4), new(11, 18), new(15.5f, 18), new(13, 28),
        new(23, 13), new(17.5f, 13)
    };

    private static Drawing.Icon FallbackIcon()
    {
        using var bmp = DrawFallback(32);
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static Drawing.Bitmap DrawFallback(int size)
    {
        var bmp = new Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);
        float s = size / 32f;
        using var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x1E, 0x1F, 0x26));
        g.FillEllipse(bg, 0, 0, size - 1, size - 1);
        var pts = new Drawing.PointF[Bolt.Length];
        for (int i = 0; i < Bolt.Length; i++) pts[i] = new Drawing.PointF(Bolt[i].X * s, Bolt[i].Y * s);
        g.FillPolygon(Drawing.Brushes.White, pts);
        return bmp;
    }

    private static ImageSource FallbackImageSource()
    {
        using var bmp = DrawFallback(64);
        using var ms = new MemoryStream();
        bmp.Save(ms, Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
