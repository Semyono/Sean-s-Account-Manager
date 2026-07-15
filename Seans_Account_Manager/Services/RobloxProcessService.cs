using System.Diagnostics;

namespace Seans_Account_Manager.Services;

public static class RobloxProcessService
{
    private static readonly string[] ProcessNames =
    {
        "RobloxPlayerBeta",
        "Roblox Game Client",
        "RobloxCrashHandler",
        "RobloxPlayerLauncher"
    };

    public static int KillAll()
    {
        int killed = 0;
        foreach (var name in ProcessNames)
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(2000);
                    killed++;
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        return killed;
    }

    public static int CountRunning()
    {
        int count = 0;
        foreach (var name in ProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            count += procs.Length;
            foreach (var p in procs) p.Dispose();
        }
        return count;
    }


}