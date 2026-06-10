using System.Windows;
using Velopack;

namespace IPlaySpeed.App;

public partial class App : Application
{
    public App()
    {
        // Velopack 설치/업데이트 훅 처리. 반드시 UI 생성 전 가장 먼저 실행되어야 한다.
        // (설치·업데이트·제거 시 Update.exe가 이 앱을 특수 인자로 실행하면 여기서 처리 후 종료된다.)
        VelopackApp.Build().Run();
    }

    /// <summary>예기치 못한 예외로 앱이 조용히 죽지 않도록 메시지로 표시.</summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "예상치 못한 오류가 발생했습니다:\n\n" + args.Exception.Message,
                "iPlaySpeed", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}
