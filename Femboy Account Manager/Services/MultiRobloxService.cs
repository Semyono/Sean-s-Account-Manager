using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace FemBoy_Account_Manager.Services;

public static class MultiRobloxService
{
    private static Mutex? _mutex;
    private static bool _mutexOwned;
    private static IntPtr _eventHandle = IntPtr.Zero;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static void Enable()
    {
        CreateProtectedMutex();
        CreateSingletonEvent();
    }

    private static void CreateProtectedMutex()
    {
        if (_mutex != null) return;
        try
        {
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(
                everyone,
                MutexRights.Synchronize | MutexRights.Modify,
                AccessControlType.Deny));

            _mutex = MutexAcl.Create(
                initiallyOwned: true,
                name: "ROBLOX_singletonEvent",
                createdNew: out bool createdNew,
                mutexSecurity: security);

            _mutexOwned = createdNew;

            if (!createdNew)
            {
                try { _mutexOwned = _mutex.WaitOne(0); }
                catch (AbandonedMutexException) { _mutexOwned = true; }
            }
        }
        catch
        {
            try
            {
                _mutex = new Mutex(true, "ROBLOX_singletonEvent", out bool createdNew);
                _mutexOwned = createdNew;
            }
            catch
            {
                _mutex = null;
                _mutexOwned = false;
            }
        }
    }

    private static void CreateSingletonEvent()
    {
        if (_eventHandle != IntPtr.Zero) return;
        _eventHandle = CreateEventW(IntPtr.Zero, true, false, "ROBLOX_singletonMutex");
    }

    public static void Disable()
    {
        try
        {
            if (_mutexOwned)
                _mutex?.ReleaseMutex();
        }
        catch { }
        _mutex?.Dispose();
        _mutex = null;
        _mutexOwned = false;

        if (_eventHandle != IntPtr.Zero)
        {
            CloseHandle(_eventHandle);
            _eventHandle = IntPtr.Zero;
        }
    }
}