using System;
using Velopack;

namespace IPlaySpeed.App;

/// <summary>
/// 명시적 진입점. Velopack 훅은 반드시 UI 생성 이전, Main 가장 처음에 실행되어야 한다
/// (설치/업데이트/제거 시 Update.exe가 특수 인자로 이 앱을 실행 → 훅 처리 후 종료).
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
