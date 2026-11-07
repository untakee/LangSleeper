using System.Runtime.InteropServices;
using System.Text;

namespace LangSleeper.Native;

internal static class Win32
{
    internal const uint WM_DESTROY = 2, WM_SIZE = 5, WM_PAINT = 15, WM_CLOSE = 16, WM_ERASEBKGND = 20,
        WM_SETTINGCHANGE = 26, WM_GETMINMAXINFO = 36, WM_DRAWITEM = 43, WM_COMMAND = 0x111,
        WM_TIMER = 0x113, WM_HOTKEY = 0x312, WM_DPICHANGED = 0x2E0, WM_APP = 0x8000;
    internal const uint WS_POPUP = 0x80000000, WS_CAPTION = 0xC00000, WS_SYSMENU = 0x80000,
        WS_MINIMIZEBOX = 0x20000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x10000;
    internal const uint EX_TOOLWINDOW = 0x80, EX_TOPMOST = 8, EX_NOACTIVATE = 0x08000000, EX_LAYERED = 0x80000,
        EX_TRANSPARENT = 0x20;
    internal const uint DT_CENTER = 1, DT_VCENTER = 4, DT_SINGLELINE = 0x20, DT_END_ELLIPSIS = 0x8000, DT_NOPREFIX = 0x800;
    internal static readonly nint Instance = GetModuleHandleW(null);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate void WinEventProc(nint hook, uint evt, nint hwnd, int objectId, int childId, uint threadId, uint time);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int x, int y, int width, int height) { Left = x; Top = y; Right = x + width; Bottom = y + height; }
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MSG { public nint Hwnd; public uint Message; public nuint WParam; public nint LParam; public uint Time; public POINT Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WNDCLASSEX
    {
        public uint Size, Style; public nint Proc; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background;
        public string? MenuName; public string ClassName; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal unsafe struct PAINTSTRUCT
    {
        public nint Dc; public int Erase; public RECT Paint; public int Restore, IncUpdate; public fixed byte Reserved[32];
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct NOTIFYICONDATA
    {
        public uint Size; public nint Hwnd; public uint Id, Flags, CallbackMessage; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct ICONINFO { public int IsIcon; public uint HotspotX, HotspotY; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFOHEADER
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter; public uint Used, Important;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFO { public BITMAPINFOHEADER Header; public uint Color; }
    [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public uint Size; public RECT Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct MARGINS { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct BLURBEHIND { public uint Flags; public int Enable; public nint Region; public int Transition; }
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO
    {
        public uint Size, Flags; public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; public RECT CaretRect;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct DRAWITEMSTRUCT
    {
        public uint ControlType, ControlId, ItemId, Action, State; public nint Hwnd, Dc; public RECT Rect; public nuint ItemData;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct INITCOMMONCONTROLSEX { public uint Size, Classes; }
    [StructLayout(LayoutKind.Sequential)] internal struct ACCENTPOLICY { public int State, Flags; public uint Color; public int Animation; }
    [StructLayout(LayoutKind.Sequential)] internal struct COMPOSITIONDATA { public int Attribute; public nint Data; public nuint Size; }
    [StructLayout(LayoutKind.Sequential)] internal struct TRACKMOUSEEVENT { public uint Size, Flags; public nint Hwnd; public uint HoverTime; }
    [DllImport("user32.dll")] internal static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tracking);
    [DllImport("user32.dll")] internal static extern bool ScreenToClient(nint hwnd, ref POINT point);
    [DllImport("user32.dll")] internal static extern bool RedrawWindow(nint hwnd, nint updateRect, nint region, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowCompositionAttribute(nint hwnd, ref COMPOSITIONDATA data);
    internal static unsafe bool TrySetAcrylic(nint window, bool dark, bool enabled = true)
    {
        var policy = new ACCENTPOLICY { State = enabled ? 4 : 0, Flags = 2, Color = dark ? 0xBB181614 : 0xBBF3F2F0 };
        var data = new COMPOSITIONDATA { Attribute = 19, Data = (nint)(&policy), Size = (nuint)sizeof(ACCENTPOLICY) };
        try { return SetWindowCompositionAttribute(window, ref data); }
        catch (EntryPointNotFoundException) { return false; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandleW(string? name);
    [DllImport("kernel32.dll", ExactSpelling = true)] internal static extern nint GetProcAddress(nint module, nint ordinal);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassExW(ref WNDCLASSEX value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowExW(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern int GetMessageW(out MSG message, nint hwnd, uint first, uint last);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessageW(ref MSG message);
    [DllImport("user32.dll")] internal static extern bool IsDialogMessageW(nint hwnd, ref MSG message);
    [DllImport("user32.dll")] internal static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool MoveWindow(nint hwnd, int x, int y, int width, int height, bool repaint);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] internal static extern nint BeginPaint(nint hwnd, out PAINTSTRUCT paint);
    [DllImport("user32.dll")] internal static extern bool EndPaint(nint hwnd, ref PAINTSTRUCT paint);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint id, ref GUITHREADINFO info);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] internal static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint SendMessageW(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] internal static extern nint SendText(nint hwnd, uint message, nuint wParam, string text);
    [DllImport("user32.dll")] internal static extern bool RegisterShellHookWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool DeregisterShellHookWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessageW(string text);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] internal static extern nuint SetTimer(nint hwnd, nuint id, uint milliseconds, nint callback);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetFocus();
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("user32.dll")] internal static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] internal static extern nint LoadIconW(nint instance, nint name);
    [DllImport("user32.dll")] internal static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] internal static extern nint CreateIconIndirect(ref ICONINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool SetWindowTextW(nint hwnd, string text);
    [DllImport("user32.dll")] internal static extern bool EnableWindow(nint hwnd, bool enabled);
    [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] internal static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] internal static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style, bool menu, uint extended, uint dpi);
    [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] internal static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("user32.dll")] internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int MessageBoxW(nint hwnd, string text, string caption, uint flags);
    [DllImport("user32.dll")] internal static extern bool SystemParametersInfoW(uint action, uint parameter, out int value, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int DrawTextW(nint dc, string text, int length, ref RECT rect, uint format);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] internal static extern nint CreateBitmap(int width, int height, uint planes, uint bitCount, nint bits);
    [DllImport("gdi32.dll")] internal static extern nint CreateDIBSection(nint dc, ref BITMAPINFO info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint GetStockObject(int index);
    [DllImport("gdi32.dll")] internal static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] internal static extern nint CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] internal static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] internal static extern uint SetBkColor(nint dc, uint color);
    [DllImport("gdi32.dll")] internal static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] internal static extern bool RoundRect(nint dc, int left, int top, int right, int bottom, int rx, int ry);
    [DllImport("gdi32.dll")] internal static extern bool Ellipse(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] internal static extern bool Rectangle(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] internal static extern bool MoveToEx(nint dc, int x, int y, nint previous);
    [DllImport("gdi32.dll")] internal static extern bool LineTo(nint dc, int x, int y);
    [DllImport("gdi32.dll")] internal static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int rx, int ry);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] internal static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeout, uint charset, uint output, uint clip, uint quality, uint family, string face);
    [DllImport("gdi32.dll")] internal static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern nint ShellExecuteW(nint hwnd, string operation, string file, string? parameters, string? directory, int show);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, uint size);
    [DllImport("dwmapi.dll")] internal static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);
    [DllImport("dwmapi.dll")] internal static extern int DwmEnableBlurBehindWindow(nint hwnd, ref BLURBEHIND blur);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] internal static extern int SetWindowTheme(nint hwnd, string? appName, string? idList);
    [DllImport("comctl32.dll")] internal static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX init);
    [DllImport("comctl32.dll")] internal static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] internal static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int LCIDToLocaleName(uint locale, StringBuilder name, int count, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern int GetLocaleInfoEx(string locale, uint type, StringBuilder data, int count);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentProcessId();
    [DllImport("user32.dll")] internal static extern uint GetGuiResources(nint process, uint flags);
}
