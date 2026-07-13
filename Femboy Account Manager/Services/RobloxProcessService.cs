using System.Diagnostics;

namespace FemBoy_Account_Manager.Services;

public static class RobloxProcessService
{
    // Process names Roblox spawns — the game client and its crash-handler leftovers
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
                catch { /* process may have exited between the query and the kill */ }
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