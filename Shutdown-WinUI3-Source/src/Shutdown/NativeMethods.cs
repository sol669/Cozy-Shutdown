using System;
using System.Runtime.InteropServices;

namespace ShutdownApp;

internal static class NativeMethods
{
    internal const uint WM_APP = 0x8000;
    internal const uint WM_COMMAND = 0x0111;
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_POWERBROADCAST = 0x0218;
    internal const uint WM_WTSSESSION_CHANGE = 0x02B1;
    internal const uint PBT_APMRESUMEAUTOMATIC = 0x0012;
    internal const uint WM_LBUTTONDBLCLK = 0x0203;
    internal const uint WM_RBUTTONUP = 0x0205;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RETURNCMD = 0x0100;
    internal const uint MF_STRING = 0x0000;
    internal const uint MF_SEPARATOR = 0x0800;
    internal const uint MF_GRAYED = 0x0001;
    internal const uint MF_POPUP = 0x0010;
    internal const uint MF_DEFAULT = 0x1000;
    internal const uint MF_CHECKED = 0x0008;
    internal const uint NIF_MESSAGE = 0x0001;
    internal const uint NIF_ICON = 0x0002;
    internal const uint NIF_TIP = 0x0004;
    internal const uint NIF_INFO = 0x0010;
    internal const uint NIIF_INFO = 0x00000001;
    internal const uint NIM_ADD = 0x00000000;
    internal const uint NIM_MODIFY = 0x00000001;
    internal const uint NIM_DELETE = 0x00000002;
    internal const uint IMAGE_ICON = 1;
    internal const uint LR_LOADFROMFILE = 0x0010;
    internal const uint LR_DEFAULTSIZE = 0x0040;
    internal const uint WTS_CURRENT_SESSION = 0xFFFFFFFF;
    internal const int WTS_CLIENT_PROTOCOL_TYPE = 16;
    internal const uint NOTIFY_FOR_THIS_SESSION = 0;
    internal const int SM_REMOTESESSION = 0x1000;
    internal const uint WM_DISPLAYCHANGE = 0x007E;

    internal delegate nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; public int Width => Right-Left; public int Height => Bottom-Top; }
    [StructLayout(LayoutKind.Sequential)] internal struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
    [StructLayout(LayoutKind.Sequential, Pack=1)] internal struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu,
        nint hInstance, nint lpParam);
    [DllImport("user32.dll")]
    internal static extern nint DefWindowProc(nint hWnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    internal static extern bool DestroyWindow(nint hWnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? lpModuleName);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
    [DllImport("user32.dll")]
    internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")]
    internal static extern bool SetMenuDefaultItem(nint hMenu, uint uItem, uint fByPos);
    [DllImport("user32.dll")]
    internal static extern uint TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);
    [DllImport("user32.dll")]
    internal static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")]
    internal static extern bool PostMessage(nint hWnd, uint Msg, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(nint hIcon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string lpString);
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSRegisterSessionNotification(nint hWnd, uint dwFlags);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSUnRegisterSessionNotification(nint hWnd);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool WTSQuerySessionInformation(nint hServer, uint sessionId, int infoClass,
        out nint ppBuffer, out uint pBytesReturned);
    [DllImport("wtsapi32.dll")]
    internal static extern void WTSFreeMemory(nint memory);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hWnd,int cmd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(nint hWnd,nuint id,uint elapse,nint proc);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint hWnd,nuint id);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern nint FindWindow(string? cls,string? name);
    [DllImport("gdi32.dll")] internal static extern int SetBkMode(nint hdc,int mode);
    [DllImport("gdi32.dll")] internal static extern uint SetTextColor(nint hdc,int color);
    [DllImport("gdi32.dll", CharSet=CharSet.Unicode)] internal static extern nint CreateFont(int h,int w,int e,int o,int weight,uint italic,uint underline,uint strike,uint charset,uint outPrec,uint clip,uint quality,uint pitch,string face);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint hdc,nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateDIBSection(nint dc,ref BITMAPINFO info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("user32.dll")] internal static extern bool UpdateLayeredWindow(nint hwnd,nint dstDc,ref POINT dst,ref SIZE size,nint srcDc,ref POINT src,uint colorKey,ref BLENDFUNCTION blend,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int DrawText(nint hdc,string text,int count,ref RECT rect,uint format);
    internal static void GetPrimaryWorkArea(out RECT rect) { rect = new RECT { Left=0, Top=0, Right=GetSystemMetrics(0), Bottom=GetSystemMetrics(1) }; }
    internal static nint FindWorkerW() => FindWindow("WorkerW", null);
}

