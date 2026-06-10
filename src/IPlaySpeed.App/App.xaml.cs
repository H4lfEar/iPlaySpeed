using System.Windows;

namespace IPlaySpeed.App;

public partial class App : Application
{
    // Velopack 훅은 Program.Main에서 처리한다(진입점 최우선 실행).

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
