using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace IPlaySpeed.App.Native;

/// <summary>
/// 실행 파일에서 아이콘을 추출해 PNG로 저장한다.
/// System.Drawing.Common 패키지에 의존하지 않고 Win32 + WPF 이미징만 사용한다.
/// </summary>
public static class IconExtractor
{
    /// <summary>
    /// exe(또는 임의 파일)의 아이콘을 PNG로 저장하고 그 경로를 반환. 실패하면 null.
    /// </summary>
    public static string? ExtractToPng(string filePath, string outputPngPath)
    {
        if (!File.Exists(filePath))
            return null;

        var info = new NativeMethods.SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            filePath, 0, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(info),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            BitmapSource bmp = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();

            Directory.CreateDirectory(Path.GetDirectoryName(outputPngPath)!);
            using var stream = new FileStream(outputPngPath, FileMode.Create, FileAccess.Write);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(stream);
            return outputPngPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    /// <summary>PNG 경로에서 BitmapImage를 로드(파일 잠금 없이). 없으면 null.</summary>
    public static BitmapImage? LoadImage(string? pngPath)
    {
        if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
            return null;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad; // 파일 잠금 방지
            img.UriSource = new Uri(pngPath, UriKind.Absolute);
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch
        {
            return null;
        }
    }
}
