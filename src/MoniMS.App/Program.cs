using MoniMS.Core.Settings;
using Velopack;

namespace MoniMS.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // 설치/업데이트/제거 중에 Velopack이 앱을 특별한 인자로 실행하므로, 무엇보다 먼저 처리해야 한다.
        VelopackApp.Build()
            // 제거 직전: "Windows 시작 시 실행" 등록을 지운다 (설정 폴더 %APPDATA%\MoniMS는 남겨 둔다)
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.Set(false, ""))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
