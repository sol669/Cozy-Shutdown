using System;
using System.Runtime.InteropServices;
using System.Linq;

namespace ShutdownApp;

internal static class RdpSession
{
    public static bool IsCurrentSessionRemote()
    {
        if (App.Preview && Environment.GetCommandLineArgs().Contains("--preview-local")) return false;
        if (App.Preview && Environment.GetCommandLineArgs().Contains("--preview-remote")) return true;
        nint buffer = nint.Zero;
        try
        {
            if (NativeMethods.WTSQuerySessionInformation(
                    nint.Zero,
                    NativeMethods.WTS_CURRENT_SESSION,
                    NativeMethods.WTS_CLIENT_PROTOCOL_TYPE,
                    out buffer,
                    out uint bytesReturned) &&
                buffer != nint.Zero &&
                bytesReturned >= sizeof(short))
            {
                return Marshal.ReadInt16(buffer) != 0;
            }
        }
        catch
        {
            // Fall back to the user32 session flag below.
        }
        finally
        {
            if (buffer != nint.Zero)
                NativeMethods.WTSFreeMemory(buffer);
        }

        return NativeMethods.GetSystemMetrics(NativeMethods.SM_REMOTESESSION) != 0;
    }
}
