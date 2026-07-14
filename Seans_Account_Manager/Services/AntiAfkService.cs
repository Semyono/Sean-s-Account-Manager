using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Seans_Account_Manager.Services;

public class AntiAfkService
{
    private const int VK_SPACE = 0x20;
    private const int VK_W    = 0x57;
    private const int SW_RESTORE = 9;

    // SendInput structures
    private const uint INPUT_KEYBOARD     = 1;
    private const uint KEYEVENTF_KEYUP    = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags;
        public uint time; public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // Lets any process call SetForegroundWindow — same trick AutoIt uses internally
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
    private const uint ASFW_ANY = unchecked((uint)-1);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public bool IsRunning => _isRunning;

    public int    IntervalMinutes { get; set; } = 10;
    public int    PressCount      { get; set; } = 1;
    public string KeyName         { get; set; } = "W";
    public ushort VirtualKeyCode  { get; set; } = VK_W;

    public event Action<int>? Ticked;
    public event Action<int>? CountdownTick;
    private int _tickCount;
    public string LastRunSummary { get; private set; } = "No runs yet.";

    public void Start()
    {
        if (_isRunning) return;
        _cts = new CancellationTokenSource();
        _isRunning = true;
        _tickCount = 0;
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _isRunning = false;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int totalSeconds = IntervalMinutes * 60;
            for (int remaining = totalSeconds; remaining > 0; remaining--)
            {
                if (ct.IsCancellationRequested) return;
                CountdownTick?.Invoke(remaining);
                try { await Task.Delay(1000, ct); }
                catch (TaskCanceledException) { return; }
            }
            if (ct.IsCancellationRequested) break;

            var stats = SendKeyToAllRobloxWindows();
            LastRunSummary = stats.ToSummary();
            _tickCount++;
            Ticked?.Invoke(_tickCount);
        }
    }

    private readonly struct SendStats
    {
        public int WindowsFound { get; init; }
        public int Focused { get; init; }
        public int KeyDownSent { get; init; }
        public int KeyUpSent { get; init; }
        public int FailedPresses { get; init; }
        public int LastWin32Error { get; init; }

        public string ToSummary()
            => $"windows={WindowsFound}, focused={Focused}, down={KeyDownSent}, up={KeyUpSent}, failed={FailedPresses}, err={LastWin32Error}";
    }

    private SendStats SendKeyToAllRobloxWindows()
    {
        int vkCode = VirtualKeyCode;
        if (vkCode == 0)
            vkCode = KeyName switch { "Space" => VK_SPACE, _ => VK_W };
        var windows = FindRobloxWindows();
        if (windows.Count == 0)
        {
            return new SendStats { WindowsFound = 0 };
        }

        IntPtr originalFg = GetForegroundWindow();
        uint myTid = GetCurrentThreadId();

        int focused = 0;
        int downSent = 0;
        int upSent = 0;
        int failed = 0;
        int lastErr = 0;

        foreach (var hwnd in windows)
        {
            try
            {
                if (IsIconic(hwnd)) { ShowWindow(hwnd, SW_RESTORE); Thread.Sleep(300); }

                // At each iteration, whoever is foreground now is our "donor" for the
                // foreground-stealing permission (on the first loop this is the user's
                // own window; on subsequent loops it's the previous Roblox window).
                IntPtr curFg    = GetForegroundWindow();
                uint   curFgTid = GetWindowThreadProcessId(curFg, out _);
                uint   targetTid = GetWindowThreadProcessId(hwnd, out _);

                AllowSetForegroundWindow(ASFW_ANY);

                // The AutoIt trick: attach to BOTH the current-foreground thread AND
                // the target thread. Attaching to the foreground thread "borrows" its
                // permission to call SetForegroundWindow — without this, Windows silently
                // ignores the call when we're not the foreground process.
                AttachThreadInput(myTid, curFgTid,  true);
                AttachThreadInput(myTid, targetTid, true);

                if (!TryFocusWindow(hwnd))
                {
                    failed += PressCount;
                    AttachThreadInput(myTid, targetTid, false);
                    AttachThreadInput(myTid, curFgTid,  false);
                    continue;
                }

                focused++;

                // Send while both thread attachments are still live
                for (int i = 0; i < PressCount; i++)
                {
                    bool down = SendKeyScan((ushort)vkCode, false, out int errDown);
                    if (!down) down = SendKeyVk((ushort)vkCode, false, out errDown);
                    Thread.Sleep(250); // hold like a real key tap — Roblox samples input per frame
                    bool up = SendKeyScan((ushort)vkCode, true, out int errUp);
                    if (!up) up = SendKeyVk((ushort)vkCode, true, out errUp);

                    if (down) downSent++; else failed++;
                    if (up) upSent++; else failed++;
                    if (!down && errDown != 0) lastErr = errDown;
                    if (!up && errUp != 0) lastErr = errUp;

                    Thread.Sleep(150);
                }

                AttachThreadInput(myTid, targetTid, false);
                AttachThreadInput(myTid, curFgTid,  false);
            }
            catch { }
        }

        // Restore the window the user was originally looking at
        Thread.Sleep(100);
        try
        {
            IntPtr curFg    = GetForegroundWindow();   // now the last Roblox window
            uint   curFgTid = GetWindowThreadProcessId(curFg,      out _);
            uint   origTid  = GetWindowThreadProcessId(originalFg, out _);

            AllowSetForegroundWindow(ASFW_ANY);
            AttachThreadInput(myTid, curFgTid, true);
            AttachThreadInput(myTid, origTid,  true);
            SetForegroundWindow(originalFg);
            SetFocus(originalFg);
            AttachThreadInput(myTid, origTid,  false);
            AttachThreadInput(myTid, curFgTid, false);
        }
        catch { }

        return new SendStats
        {
            WindowsFound = windows.Count,
            Focused = focused,
            KeyDownSent = downSent,
            KeyUpSent = upSent,
            FailedPresses = failed,
            LastWin32Error = lastErr
        };
    }

    private bool TryFocusWindow(IntPtr hwnd)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);
            Thread.Sleep(40);

            if (GetForegroundWindow() == hwnd)
            {
                Thread.Sleep(120);
                return true;
            }
        }

        return GetForegroundWindow() == hwnd;
    }

    // Send with BOTH the VK code and the hardware scan code, exactly like a real
    // keyboard does. Roblox reads input through Raw Input, which surfaces the SCAN
    // code — sending wVk with wScan=0 makes the game see "scan code 0" = nothing.
    private static bool SendKeyScan(ushort vkCode, bool keyUp, out int win32Error)
    {
        win32Error = 0;
        ushort scan = (ushort)MapVirtualKey(vkCode, 0); // MAPVK_VK_TO_VSC
        var inputs = new INPUT[1];
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = KEYEVENTF_SCANCODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        uint sent = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 1) return true;

        win32Error = Marshal.GetLastWin32Error();

        // Fallback path used by some tools (AutoIt family) when SendInput gets blocked.
        // Even if this API is older, it can still work in edge cases where SendInput fails.
        try
        {
            uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
            keybd_event((byte)vkCode, (byte)scan, flags, UIntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SendKeyVk(ushort vkCode, bool keyUp, out int win32Error)
    {
        win32Error = 0;
        ushort scan = (ushort)MapVirtualKey(vkCode, 0); // MAPVK_VK_TO_VSC
        var inputs = new INPUT[1];
        inputs[0] = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk   = vkCode,
                    wScan = scan,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        uint sent = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        if (sent == 1) return true;

        win32Error = Marshal.GetLastWin32Error();

        try
        {
            uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
            keybd_event((byte)vkCode, (byte)scan, flags, UIntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Fires one key-send cycle immediately (for testing without waiting the interval).
    public async Task<string> TestNowAsync()
    {
        var stats = await Task.Run(SendKeyToAllRobloxWindows);
        LastRunSummary = stats.ToSummary();
        return LastRunSummary;
    }

    public int GetOpenRobloxWindowCount() => FindRobloxWindows().Count;

    private List<IntPtr> FindRobloxWindows()
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
                if (!proc.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase))
                    return true;

                // Accept any visible, titled window — title may be the account username
                // if WindowRenameService has renamed it.
                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);
                if (title.Length > 0)
                    result.Add(hWnd);
            }
            catch { }

            return true;
        }, IntPtr.Zero);
        return result;
    }
}