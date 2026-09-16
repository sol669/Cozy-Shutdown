using System;
using System.Runtime.InteropServices;

namespace ShutdownApp;

internal static class DesktopClockService
{
    private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_POPUP = 0x80000000, WM_PAINT = 0x000F, WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0, WM_TIMER = 0x0113, WM_SYSCOMMAND = 0x0112;
    private const nuint SC_MINIMIZE = 0xF020;
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040, HWND_BOTTOM = 1;
    private static SettingsStore? _settings;
    private static nint _hwnd, _desktopOwner, _memoryDc, _highBitmap, _finalBitmap, _highBits, _finalBits, _originalBitmap;
    private static NativeMethods.WndProc? _proc;
    private const string WindowClass = "CozyShutdown.DesktopClock";
    private static int _width, _height, _x, _y, _surfaceWidth, _surfaceHeight;
    private static string? _renderKey;
    private static DateTime _nextRenderAt = DateTime.MinValue;
    private static bool _positioned;
    private static int _timeSize, _dateSize, _lineGap;

    public static void Initialize(SettingsStore settings)
    {
        _settings = settings;
        if (settings.Current.ShowClock) Create();
    }

    public static void Refresh()
    {
        if (_settings?.Current.ShowClock == true)
        {
            if (_hwnd == nint.Zero) Create();
            else Position();
        }
        else Destroy();
    }

    public static void InvalidateDisplayLayout()
    {
        _positioned = false;
        _renderKey = null;
        _nextRenderAt = DateTime.MinValue;
        if (_hwnd != nint.Zero) Position(force: true);
    }

    public static void RecreateAfterShellRestart()
    {
        if (_settings?.Current.ShowClock != true) return;
        Destroy();
        Create();
    }

    public static void Dispose() => Destroy();

    private static void Create()
    {
        if (_hwnd != nint.Zero) return;
        try
        {
            _proc = WndProc;
            var windowClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = _proc,
                hInstance = NativeMethods.GetModuleHandle(null),
                lpszClassName = WindowClass
            };
            NativeMethods.RegisterClassEx(ref windowClass);

            _desktopOwner = NativeMethods.FindWindow("Progman", null);
            if (_desktopOwner == nint.Zero) _desktopOwner = NativeMethods.FindWorkerW();
            _hwnd = NativeMethods.CreateWindowEx((uint)(WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE),
                WindowClass, string.Empty, WS_POPUP, 0, 0, 0, 0, _desktopOwner, 0, windowClass.hInstance, 0);
            if (_hwnd == nint.Zero) throw new InvalidOperationException("Could not create the desktop clock window.");

            Position(force: true);
            NativeMethods.SetTimer(_hwnd, 1, 1000, 0);
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            Destroy();
        }
    }

    private static void Position(bool force = false)
    {
        if (_hwnd == nint.Zero) return;

        NativeMethods.GetPrimaryWorkArea(out var area);
        double visualScale = Math.Clamp(area.Width / 3840d * (_settings?.Current.ClockScale ?? 100) / 100d, .25, 2.0);
        int width = Math.Max(700, (int)Math.Round(1200 * visualScale));
        int timeSize = Math.Max(44, (int)Math.Round(220 * visualScale));
        int dateSize = Math.Max(14, (int)Math.Round(48 * visualScale));
        int lineGap = Math.Max(10, (int)Math.Round(56 * visualScale));
        bool calendar = _settings?.Current.ShowCalendar ?? true;
        int height = timeSize + (calendar ? lineGap + dateSize : 0) + Math.Max(8, (int)Math.Round(12 * visualScale));
        int x = area.Left + (int)Math.Round(area.Width * ((_settings?.Current.ClockPositionX ?? 50) / 100d)) - width / 2;
        int y = area.Top + (int)Math.Round(area.Height * ((_settings?.Current.ClockPositionY ?? 25) / 100d)) - height / 2;

        bool geometryChanged = width != _width || height != _height || timeSize != _timeSize || dateSize != _dateSize || lineGap != _lineGap;
        bool locationChanged = x != _x || y != _y;
        _width = width;
        _height = height;
        _timeSize = timeSize;
        _dateSize = dateSize;
        _lineGap = lineGap;
        _x = x;
        _y = y;

        if (geometryChanged) _renderKey = null;
        RenderLayered();

        // The one-second timer is retained only as a lightweight Win+D recovery check.
        // It no longer moves or redraws an already visible, unchanged widget.
        if (force || !_positioned || geometryChanged || locationChanged || !NativeMethods.IsWindowVisible(_hwnd))
        {
            NativeMethods.ShowWindow(_hwnd, 4); // SW_SHOWNOACTIVATE
            NativeMethods.SetWindowPos(_hwnd, (nint)HWND_BOTTOM, _x, _y, _width, _height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _positioned = true;
        }
    }

    private static void Destroy()
    {
        if (_hwnd != nint.Zero)
        {
            NativeMethods.KillTimer(_hwnd, 1);
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }
        ReleaseRenderSurface();
        _desktopOwner = nint.Zero;
        _renderKey = null;
        _nextRenderAt = DateTime.MinValue;
        _positioned = false;
    }

    private static void Tick()
    {
        if (_hwnd == nint.Zero) return;
        bool hidden = !NativeMethods.IsWindowVisible(_hwnd);
        if (hidden || DateTime.Now >= _nextRenderAt) Position(force: hidden);
    }

    private static nint WndProc(nint hwnd, uint msg, nuint wp, nint lp)
    {
        if (msg == 0x0084) return (nint)(-1); // HTTRANSPARENT: desktop icons and applications receive input.
        if (msg == WM_SYSCOMMAND && (wp & 0xFFF0) == SC_MINIMIZE) return 0;
        if (msg == WM_TIMER) { Tick(); return 0; }
        if (msg == WM_DISPLAYCHANGE || msg == WM_DPICHANGED) { InvalidateDisplayLayout(); return 0; }
        if (msg == WM_PAINT) return NativeMethods.DefWindowProc(hwnd, msg, wp, lp);
        return NativeMethods.DefWindowProc(hwnd, msg, wp, lp);
    }

    private static unsafe void RenderLayered()
    {
        if (_hwnd == nint.Zero || _width <= 0 || _height <= 0) return;
        DateTime now = DateTime.Now;
        string key = $"{now:yyyyMMddHHmm}|{_width}x{_height}|{_timeSize}|{_dateSize}|{_lineGap}|{_settings?.Current.ShowCalendar}|{_settings?.Current.ClockTextColor}|{_settings?.Current.ClockOpacity}";
        if (_renderKey == key) return;

        try
        {
            EnsureRenderSurface();
            new Span<byte>((void*)_highBits, _surfaceWidth * _surfaceHeight * 4).Clear();

            nint previousObject = NativeMethods.SelectObject(_memoryDc, _highBitmap);
            nint timeFont = 0;
            nint dateFont = 0;
            try
            {
                NativeMethods.SetBkMode(_memoryDc, 1);
                NativeMethods.SetTextColor(_memoryDc, 0xFFFFFF);
                var culture = _settings?.Current.Language == AppLanguage.Russian
                    ? new System.Globalization.CultureInfo("ru-RU")
                    : new System.Globalization.CultureInfo("en-US");
                string time = now.ToString("HH:mm", culture);
                string date = now.ToString("dddd, d MMMM", culture);
                var rect = new NativeMethods.RECT { Left = 0, Top = 0, Right = _surfaceWidth, Bottom = _surfaceHeight };

                timeFont = NativeMethods.CreateFont(-_timeSize * 4, 0, 0, 0, 300, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI Light");
                if (timeFont == nint.Zero) throw new InvalidOperationException("Could not create the clock font.");
                NativeMethods.SelectObject(_memoryDc, timeFont);
                var timeRect = rect;
                NativeMethods.DrawText(_memoryDc, time, -1, ref timeRect, 0x00000001 | 0x00000004 | 0x00000800);

                if (_settings?.Current.ShowCalendar ?? true)
                {
                    dateFont = NativeMethods.CreateFont(-_dateSize * 4, 0, 0, 0, 300, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI Light");
                    if (dateFont == nint.Zero) throw new InvalidOperationException("Could not create the calendar font.");
                    NativeMethods.SelectObject(_memoryDc, dateFont);
                    var dateRect = rect;
                    dateRect.Top = (_timeSize + _lineGap) * 4;
                    NativeMethods.DrawText(_memoryDc, date, -1, ref dateRect, 0x00000001 | 0x00000004 | 0x00000800);
                }
            }
            finally
            {
                NativeMethods.SelectObject(_memoryDc, previousObject);
                if (timeFont != nint.Zero) NativeMethods.DeleteObject(timeFont);
                if (dateFont != nint.Zero) NativeMethods.DeleteObject(dateFont);
            }

            int rgb = ParseRgb(_settings?.Current.ClockTextColor);
            int opacity = Math.Clamp(_settings?.Current.ClockOpacity ?? 100, 0, 100);
            byte* high = (byte*)_highBits;
            byte* final = (byte*)_finalBits;
            int red = (rgb >> 16) & 255, green = (rgb >> 8) & 255, blue = rgb & 255;
            for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
            {
                int sum = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    int index = (((y * 4 + sy) * _surfaceWidth) + (x * 4 + sx)) * 4;
                    sum += Math.Max(high[index], Math.Max(high[index + 1], high[index + 2]));
                }
                int alpha = ((sum + 8) / 16) * opacity / 100;
                int output = (y * _width + x) * 4;
                final[output] = (byte)(blue * alpha / 255);
                final[output + 1] = (byte)(green * alpha / 255);
                final[output + 2] = (byte)(red * alpha / 255);
                final[output + 3] = (byte)alpha;
            }

            NativeMethods.SelectObject(_memoryDc, _finalBitmap);
            nint screen = NativeMethods.GetDC(0);
            try
            {
                var destination = new NativeMethods.POINT { X = _x, Y = _y };
                var source = new NativeMethods.POINT();
                var size = new NativeMethods.SIZE { cx = _width, cy = _height };
                var blend = new NativeMethods.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                NativeMethods.UpdateLayeredWindow(_hwnd, screen, ref destination, ref size, _memoryDc, ref source, 0, ref blend, 2);
            }
            finally
            {
                if (screen != nint.Zero) NativeMethods.ReleaseDC(0, screen);
            }
            _renderKey = key;
            _nextRenderAt = now.AddMinutes(1);
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            _renderKey = null;
        }
    }

    private static void EnsureRenderSurface()
    {
        const int supersample = 4;
        int renderWidth = _width * supersample;
        int renderHeight = _height * supersample;
        if (_memoryDc != nint.Zero && renderWidth == _surfaceWidth && renderHeight == _surfaceHeight) return;

        ReleaseRenderSurface();
        nint screen = NativeMethods.GetDC(0);
        try { _memoryDc = NativeMethods.CreateCompatibleDC(screen); }
        finally { if (screen != nint.Zero) NativeMethods.ReleaseDC(0, screen); }
        if (_memoryDc == nint.Zero) throw new InvalidOperationException("Could not create the clock drawing surface.");

        try
        {
            _surfaceWidth = renderWidth;
            _surfaceHeight = renderHeight;
            var highInfo = BitmapInfo(renderWidth, renderHeight);
            _highBitmap = NativeMethods.CreateDIBSection(_memoryDc, ref highInfo, 0, out _highBits, 0, 0);
            var finalInfo = BitmapInfo(_width, _height);
            _finalBitmap = NativeMethods.CreateDIBSection(_memoryDc, ref finalInfo, 0, out _finalBits, 0, 0);
            if (_highBitmap == nint.Zero || _finalBitmap == nint.Zero || _highBits == nint.Zero || _finalBits == nint.Zero)
                throw new InvalidOperationException("Could not allocate the clock drawing surface.");
            _originalBitmap = NativeMethods.SelectObject(_memoryDc, _highBitmap);
        }
        catch
        {
            ReleaseRenderSurface();
            throw;
        }
    }

    private static void ReleaseRenderSurface()
    {
        if (_memoryDc != nint.Zero && _originalBitmap != nint.Zero)
            NativeMethods.SelectObject(_memoryDc, _originalBitmap);
        if (_highBitmap != nint.Zero) NativeMethods.DeleteObject(_highBitmap);
        if (_finalBitmap != nint.Zero) NativeMethods.DeleteObject(_finalBitmap);
        if (_memoryDc != nint.Zero) NativeMethods.DeleteDC(_memoryDc);
        _memoryDc = _highBitmap = _finalBitmap = _highBits = _finalBits = _originalBitmap = nint.Zero;
        _surfaceWidth = _surfaceHeight = 0;
    }

    private static NativeMethods.BITMAPINFO BitmapInfo(int width, int height) => new()
    {
        bmiHeader = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        }
    };

    private static int ParseRgb(string? value)
    {
        try { return Convert.ToInt32((value ?? "#FFFFFF").TrimStart('#'), 16); }
        catch { return 0xFFFFFF; }
    }
}
