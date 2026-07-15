using System.Windows;
using Seans_Account_Manager.Services;

namespace Seans_Account_Manager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MultiRobloxService.Enable();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RobloxProcessService.KillAll();
        MultiRobloxService.Disable();
        base.OnExit(e);
    }
}