using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShutdownApp;

public static class SystemActions
{
    private const uint EWX_LOGOFF = 0x00000000;
    private const uint EWX_SHUTDOWN = 0x00000001;
    private const uint EWX_REBOOT = 0x00000002;
    private const uint EWX_FORCEIFHUNG = 0x00000010;
    private const uint EWX_POWEROFF = 0x00000008;

    private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    public static bool IsAvailable(PowerActionKind action) => action switch
    {
        PowerActionKind.Sleep => IsPwrSuspendAllowed(),
        PowerActionKind.Hibernate => IsPwrHibernateAllowed(),
        _ => true
    };

    public static void Execute(PowerActionKind action)
    {
        // Preview uses the real UI but can never invoke any OS action.
        if (App.Preview) return;
        if (!IsAvailable(action)) throw new InvalidOperationException("Action is unavailable.");
        switch (action)
        {
            case PowerActionKind.Shutdown:
                ExitWindows(EWX_SHUTDOWN | EWX_POWEROFF | EWX_FORCEIFHUNG);
                break;
            case PowerActionKind.Restart:
                ExitWindows(EWX_REBOOT | EWX_FORCEIFHUNG);
                break;
            case PowerActionKind.Sleep:
                Suspend(false);
                break;
            case PowerActionKind.Hibernate:
                Suspend(true);
                break;
            case PowerActionKind.Lock:
                if (!LockWorkStation()) throw new Win32Exception(Marshal.GetLastWin32Error());
                break;
            case PowerActionKind.Disconnect:
                if (!RdpSession.IsCurrentSessionRemote()) return;
                DisconnectRdp();
                break;
        }
    }

    private static void Suspend(bool hibernate)
    {
        EnableShutdownPrivilege();
        if (!SetSuspendState(hibernate, false, false))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void DisconnectRdp()
    {
        if (App.Preview) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "tsdiscon.exe",
            UseShellExecute = true
        });
    }

    private static void ExitWindows(uint flags)
    {
        EnableShutdownPrivilege();
        if (!ExitWindowsEx(flags, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out nint token))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out LUID luid))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            TOKEN_PRIVILEGES privileges = new()
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
            };

            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, nint.Zero, nint.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();
    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
    [DllImport("powrprof.dll")]
    private static extern bool IsPwrSuspendAllowed();
    [DllImport("powrprof.dll")]
    private static extern bool IsPwrHibernateAllowed();
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint ProcessHandle, uint DesiredAccess, out nint TokenHandle);
    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(nint TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, nint PreviousState, nint ReturnLength);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint hObject);
}
