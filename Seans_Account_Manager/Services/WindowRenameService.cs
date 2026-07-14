using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Seans_Account_Manager.Services;

public static class WindowRenameService
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static readonly HashSet<IntPtr> _renamed = new();
    public static async Task RenameNextRobloxWindowAsync(string username, int timeoutSeconds = 30)
    {
        var existingBefore = FindRobloxWindows();

        var start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalSeconds < timeoutSeconds)
        {
            await Task.Delay(1000);

            var current = FindRobloxWindows();
            foreach (var hwnd in current)
            {
                if (existingBefore.Contains(hwnd)) continue;
                if (_renamed.Contains(hwnd)) continue;

                await Task.Delay(500);
                string newTitle = $"Roblox - {username}";
                if (SetWindowText(hwnd, newTitle))
                {
                    _renamed.Add(hwnd);
                    return;
                }
            }
        }
    }

    private static List<IntPtr> FindRobloxWindows()
    {
        var result = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                if (proc.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase))
                {

                    var title = new StringBuilder(256);
                    GetWindowText(hWnd, title, title.Capacity);
                    if (title.ToString().StartsWith("Roblox", StringComparison.OrdinalIgnoreCase))
                        result.Add(hWnd);
                }
            }
            catch { /* process might have exited between enumeration and lookup */ }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static void ClearTracking() => _renamed.Clear();
}