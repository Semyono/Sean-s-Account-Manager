using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Seans_Account_Manager.Services;

public static class RobloxTweaksService
{
    private const string RobloxProcessName = "RobloxPlayerBeta";

    public static string? LastError { get; private set; }

    private static string? GlobalSettingsPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Roblox", "GlobalBasicSettings_13.xml");
    }

    public static bool SetFramerateCap(int fps)
    {
        LastError = null;
        try
        {
            string? path = GlobalSettingsPath();
            if (path == null)
            {
                LastError = "Could not resolve LocalAppData path.";
                return false;
            }

            bool fileExists = File.Exists(path);
            if (fileExists)
                File.SetAttributes(path, FileAttributes.Normal);
            else
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            string content = fileExists ? File.ReadAllText(path) : DefaultXml(fps);
            content = UpsertFramerateCap(content, fps);

            File.WriteAllText(path, content);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public static bool UnlockFramerateCap()
    {
        LastError = null;
        try
        {
            string? path = GlobalSettingsPath();
            if (path == null || !File.Exists(path)) return true;

            File.SetAttributes(path, FileAttributes.Normal);
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static string UpsertFramerateCap(string xml, int fps)
    {
        var pattern = new Regex(@"(<int\s+name=""FramerateCap"">)-?\d+(</int>)", RegexOptions.IgnoreCase);
        if (pattern.IsMatch(xml))
            return pattern.Replace(xml, $"${{1}}{fps}${{2}}");

        int idx = xml.LastIndexOf("</Properties>", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return DefaultXml(fps);
        return xml.Insert(idx, $"  <int name=\"FramerateCap\">{fps}</int>\n");
    }

    private static string DefaultXml(int fps) =>
        $"<Properties>\n  <int name=\"FramerateCap\">{fps}</int>\n</Properties>\n";


    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSizeEx(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x2;
    private const uint QUOTA_LIMITS_HARDWS_MAX_ENABLE = 0x4;

    private static CancellationTokenSource? _ramBoostCts;
    private static bool _ramBoostRunning;
    private static readonly HashSet<int> _hardCappedPids = new();

    public static bool IsRamBoostRunning => _ramBoostRunning;

    public static void StartRamBoost(int limitMb, Action<string>? log = null)
    {
        if (_ramBoostRunning) return;
        _ramBoostCts = new CancellationTokenSource();
        _ramBoostRunning = true;
        _hardCappedPids.Clear();
        var token = _ramBoostCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var currentPids = new HashSet<int>();

                    foreach (var proc in System.Diagnostics.Process.GetProcessesByName(RobloxProcessName))
                    {
                        try
                        {
                            currentPids.Add(proc.Id);
                            long memMb = proc.WorkingSet64 / 1024 / 1024;

                            IntPtr handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, proc.Id);
                            if (handle == IntPtr.Zero) continue;

                            try
                            {
                                if (!_hardCappedPids.Contains(proc.Id))
                                {
                                    IntPtr maxBytes = new IntPtr(limitMb * 1024L * 1024L);
                                    IntPtr minBytes = new IntPtr(32L * 1024L * 1024L);

                                    bool ok = SetProcessWorkingSetSizeEx(handle, minBytes, maxBytes,
                                        QUOTA_LIMITS_HARDWS_MIN_DISABLE | QUOTA_LIMITS_HARDWS_MAX_ENABLE);

                                    if (ok)
                                    {
                                        _hardCappedPids.Add(proc.Id);
                                        log?.Invoke($"Hard RAM cap installed for Roblox PID {proc.Id} (max {limitMb} MB).");
                                    }
                                }

                                if (memMb >= limitMb)
                                {
                                    EmptyWorkingSet(handle);
                                    log?.Invoke($"Trimmed RAM for Roblox PID {proc.Id} ({memMb} MB -> capped at {limitMb} MB).");
                                }
                            }
                            finally
                            {
                                CloseHandle(handle);
                            }
                        }
                        catch { }
                        finally { proc.Dispose(); }
                    }

                    _hardCappedPids.IntersectWith(currentPids);
                }
                catch { }

                try { await Task.Delay(10000, token); }
                catch (TaskCanceledException) { break; }
            }
            _ramBoostRunning = false;
        }, token);
    }

    public static void StopRamBoost()
    {
        _ramBoostCts?.Cancel();
        _ramBoostRunning = false;
        _hardCappedPids.Clear();
    }
}