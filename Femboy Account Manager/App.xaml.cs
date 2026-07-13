using System.Windows;
using FemBoy_Account_Manager.Services;

namespace FemBoy_Account_Manager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MultiRobloxService.Enable();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up zombie Roblox processes on shutdown
        RobloxProcessService.KillAll();
        MultiRobloxService.Disable();
        base.OnExit(e);
    }
}