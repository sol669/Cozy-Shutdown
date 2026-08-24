using System;
using System.Runtime.InteropServices;

namespace ShutdownApp;

internal static class NativeMethods
{
    internal const uint WM_APP = 0x8000;
    internal const uint WM_COMMAND = 0x0111;
    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint WM_POWERBROADCAST = 0x0218;
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
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint VK_Q = 0x51;
    internal const uint NIF_MESSAGE = 0x0001;
    internal const uint NIF_ICON = 0x0002;
    internal const uint NIF_TIP = 0x0004;
    internal const uint NIF_INFO = 0x0010;
    internal const uint NIIF_INFO = 0x00000001;
    internal const uint NIM_ADD = 0x00000000;
    internal const uint NIM_MODIFY = 0x00000001;
    internal const uint NIM_DELETE = 0x00000002;
    internal const uint NIM_SETVERSION = 0x00000004;
    internal const uint NOTIFYICON_VERSION_4 = 4;
    internal const uint IMAGE_ICON = 1;
    internal const uint LR_LOADFROMFILE = 0x0010;
    internal const uint LR_DEFAULTSIZE = 0x0040;
    internal const int SM_REMOTESESSION = 0x1000;
    internal const int IDI_APPLICATION = 32512;

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
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint fuLoad);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint LoadIcon(nint hInstance, nint lpIconName);
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(nint hIcon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string lpString);
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int nIndex);
}
