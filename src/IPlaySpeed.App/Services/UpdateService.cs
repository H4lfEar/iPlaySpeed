using System.Windows;
using Velopack;
using Velopack.Sources;

namespace IPlaySpeed.App.Services;

/// <summary>
/// GitHub Releases를 소스로 Velopack 자동 업데이트를 처리한다.
/// 새 버전이 있으면 사용자에게 물어보고, 동의 시 (델타) 다운로드 후 적용·재시작한다.
/// Velopack으로 설치된 경우에만 동작하며, 포터블/개발 실행에서는 조용히 건너뛴다.
/// </summary>
public static class UpdateService
{
    private const string RepoUrl = "https://github.com/H4lfEar/iPlaySpeed";

    public static async Task CheckAsync(bool silentIfNone = true)
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));

            // Velopack으로 설치된 경우에만 업데이트 가능(포터블 단일 exe·개발 실행은 제외).
            if (!mgr.IsInstalled)
            {
                if (!silentIfNone)
                    MessageBox.Show(
                        "이 빌드는 자동 업데이트를 지원하지 않습니다.\n\n" +
                        "개발 빌드 또는 포터블(단일 exe) 실행으로 보입니다.\n" +
                        "GitHub 릴리스의 'iPlaySpeed-win-Setup.exe'로 설치한 버전에서 업데이트가 동작합니다.",
                        "iPlaySpeed 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            UpdateInfo? info = await mgr.CheckForUpdatesAsync().ConfigureAwait(true);
            if (info is null)
            {
                if (!silentIfNone)
                    MessageBox.Show("최신 버전을 사용 중입니다.", "iPlaySpeed 업데이트",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ver = info.TargetFullRelease.Version;
            var ask = MessageBox.Show(
                $"새 버전 {ver} 이(가) 있습니다.\n지금 다운로드하고 업데이트할까요?\n(완료 후 앱이 재시작됩니다)",
                "iPlaySpeed 업데이트", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (ask != MessageBoxResult.Yes)
                return;

            await mgr.DownloadUpdatesAsync(info).ConfigureAwait(true);
            mgr.ApplyUpdatesAndRestart(info); // 적용 후 자동 재시작(프로세스 종료)
        }
        catch
        {
            // 네트워크/소스 오류 등은 조용히 무시(앱 사용에는 지장 없음).
            if (!silentIfNone)
                MessageBox.Show("업데이트 확인 중 문제가 발생했습니다.", "iPlaySpeed 업데이트",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
