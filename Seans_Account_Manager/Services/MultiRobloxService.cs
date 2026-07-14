using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;

namespace Seans_Account_Manager.Services;

public static class MultiRobloxService
{
    private const string RobloxProcessName = "RobloxPlayerBeta";
    private const string SingletonHandleName = "ROBLOX_singletonEvent";
    private const string HandleToolName = "handle64.exe";
    private const string FallbackHandleToolName = "handle.exe";
    private const string HandleDownloadUrl = "https://download.sysinternals.com/files/Handle.zip";

    private static readonly object SyncRoot = new();
    private static Thread? _monitorThread;
    private static volatile bool _monitoring;
    private static string? _handleToolPath;
    private static bool _enabled;

    public static string? LastError { get; private set; }
    public static bool IsEnabled => _enabled;

    public static bool Enable()
    {
        lock (SyncRoot)
        {
            if (_monitoring) return true;

            LastError = null;
            var toolPath = ResolveHandleToolPath();
            if (string.IsNullOrEmpty(toolPath))
            {
                LastError = "Handle64 not found.";
                return false;
            }

            _handleToolPath = toolPath;
            _monitoring = true;
            _enabled = true;
            _monitorThread = new Thread(MonitorRobloxProcesses)
            {
                IsBackground = true,
                Name = "MultiRobloxHandleMonitor"
            };
            _monitorThread.Start();

            ProbeExistingProcesses();
            return true;
        }
    }

    public static bool IsHandle64Available()
    {
        lock (SyncRoot)
        {
            return !string.IsNullOrEmpty(ResolveHandleToolPath());
        }
    }

    public static bool DownloadHandle64()
    {
        lock (SyncRoot)
        {
            LastError = null;
            if (!TryDownloadHandleTool(out var path) || string.IsNullOrWhiteSpace(path))
            {
                if (string.IsNullOrWhiteSpace(LastError))
                    LastError = "Failed to download Handle64.";
                return false;
            }

            _handleToolPath = path;
            return true;
        }
    }

    public static void Disable()
    {
        lock (SyncRoot)
        {
            if (!_monitoring) return;

            _monitoring = false;
            _enabled = false;
            _monitorThread?.Join(1000);
            _monitorThread = null;
            _handleToolPath = null;
            LastError = null;
        }
    }

    private static void MonitorRobloxProcesses()
    {
        var seen = new HashSet<int>();

        while (_monitoring)
        {
            try
            {
                var current = GetRobloxProcessIds();
                var newPids = current.Where(pid => !seen.Contains(pid)).ToList();

                foreach (var pid in newPids)
                {
                    seen.Add(pid);
                    CloseSingletonHandle(pid);
                }

                seen = current.ToHashSet();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            Thread.Sleep(500);
        }
    }

    private static void ProbeExistingProcesses()
    {
        foreach (var pid in GetRobloxProcessIds())
        {
            CloseSingletonHandle(pid);
        }
    }

    private static HashSet<int> GetRobloxProcessIds()
    {
        var result = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName(RobloxProcessName))
        {
            try
            {
                if (!process.HasExited)
                    result.Add(process.Id);
            }
            catch { }
            finally
            {
                try { process.Dispose(); } catch { }
            }
        }
        return result;
    }

    private static void CloseSingletonHandle(int pid)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(_handleToolPath)) return;

        for (int attempt = 0; attempt < 5 && _monitoring; attempt++)
        {
            if (TryGetSingletonHandleValue(pid, out var handleValue))
            {
                TryCloseHandleValue(pid, handleValue);
                return;
            }

            if (attempt < 4)
                Thread.Sleep(1000);
        }
    }

    private static bool TryGetSingletonHandleValue(int pid, out string? handleValue)
    {
        handleValue = null;
        if (string.IsNullOrWhiteSpace(_handleToolPath)) return false;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _handleToolPath,
                Arguments = $"-accepteula -p {pid} -a",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(error))
            {
                LastError = error.Trim();
                return false;
            }

            foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.Contains(SingletonHandleName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = Regex.Match(line, @"([0-9A-Fa-f]+):.*" + Regex.Escape(SingletonHandleName), RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    handleValue = match.Groups[1].Value;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        return false;
    }

    private static void TryCloseHandleValue(int pid, string? handleValue)
    {
        if (string.IsNullOrWhiteSpace(_handleToolPath) || string.IsNullOrWhiteSpace(handleValue)) return;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _handleToolPath,
                Arguments = $"-accepteula -p {pid} -c {handleValue} -y",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            process.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private static string? ResolveHandleToolPath()
    {
        foreach (var candidate in GetHandleToolCandidates())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetHandleToolCandidates()
    {
        var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SeansAccountManager");
        yield return Path.Combine(appDataDir, HandleToolName);
        yield return Path.Combine(appDataDir, FallbackHandleToolName);
        yield return Path.Combine(AppContext.BaseDirectory, HandleToolName);
        yield return Path.Combine(AppContext.BaseDirectory, FallbackHandleToolName);
    }

    private static bool TryDownloadHandleTool(out string? path)
    {
        path = null;
        try
        {
            var toolDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SeansAccountManager");
            Directory.CreateDirectory(toolDir);

            using var client = new HttpClient();
            var archiveBytes = client.GetByteArrayAsync(HandleDownloadUrl).GetAwaiter().GetResult();
            var archivePath = Path.Combine(toolDir, "Handle.zip");
            File.WriteAllBytes(archivePath, archiveBytes);

            using var archive = ZipFile.OpenRead(archivePath);
            var entry = archive.Entries.FirstOrDefault(e => e.FullName.Equals(HandleToolName, StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e => e.FullName.Equals(FallbackHandleToolName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                return false;

            var destinationPath = Path.Combine(toolDir, HandleToolName);
            using (var source = entry.Open())
            using (var destination = File.Create(destinationPath))
            {
                source.CopyTo(destination);
            }

            path = destinationPath;
            return File.Exists(destinationPath);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }
}