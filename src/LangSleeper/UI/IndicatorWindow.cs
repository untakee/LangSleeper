using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LangSleeper.Core;
using LangSleeper.Native;

namespace LangSleeper.UI;

/// <summary>A short-lived compositor window. Its fade never blocks the hotkey message loop.</summary>
internal sealed class IndicatorWindow : IDisposable
{
    private readonly object gate = new();
    private DisplayRequest? pending;
    private Thread? worker;
    private nint window;
    private int visible;
    private bool disposed;
    private string currentCode = "";
    private string? lastError;
    private const uint PresentMessage = Win32.WM_APP + 41, StopMessage = Win32.WM_APP + 42;

    private sealed record DisplayRequest(string Code, bool Dark, bool Animate, bool Transparent, double Scale, Win32.RECT WorkArea, int Duration);
    internal bool IsVisible => Volatile.Read(ref visible) != 0;
    internal string DisplayedCode => Volatile.Read(ref currentCode);
    internal string? LastError => Volatile.Read(ref lastError);
    internal nint Handle => Volatile.Read(ref window);

    internal void Show(KeyboardLayout layout, bool primary, Configuration config) => Queue(layout, config, 1000);

    // Lets the existing test executable hold the real native surface long enough for visual QA.
    // Production switching always uses the ordinary one-second Show method.
    internal void ShowForDiagnostics(KeyboardLayout layout, Configuration config, int durationMilliseconds = 5000)
        => Queue(layout, config, Math.Clamp(durationMilliseconds, 250, 30000));

    private void Queue(KeyboardLayout layout, Configuration config, int duration)
    {
        var foreground = Win32.GetForegroundWindow();
        var monitor = Win32.MonitorFromWindow(foreground, 2);
        var info = new Win32.MONITORINFO { Size = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfoW(monitor, ref info)) return;
        var dpi = foreground == 0 ? Win32.GetDpiForSystem() : Win32.GetDpiForWindow(foreground);
        var request = new DisplayRequest(IndicatorFrame.NormalizeCode(layout.Code),
            config.Theme == "dark" || config.Theme == "system" && Palette.SystemDark(),
            config.Animate && Palette.AnimationsEnabled, Palette.TransparencyEnabled(),
            Math.Clamp(dpi / 96.0, 0.75, 4), info.Work, duration);
        Thread? start = null;
        nint target;
        lock (gate)
        {
            if (disposed) return;
            pending = request;
            target = window;
            if (worker is null)
            {
                worker = start = new Thread(Run, 192 * 1024) { IsBackground = true, Name = "LangSleeper indicator" };
            }
        }
        start?.Start();
        if (target != 0) Win32.PostMessageW(target, PresentMessage, 0, 0);
    }

    private void Run()
    {
        try { new Host(this).Run(); }
        catch (Exception ex) { Volatile.Write(ref lastError, ex.Message); }
        finally
        {
            lock (gate)
            {
                // A new request can have started a replacement host while the old one unwound.
                if (worker == Thread.CurrentThread) { Volatile.Write(ref visible, 0); worker = null; window = 0; }
            }
        }
    }

    private DisplayRequest? TakePending()
    {
        lock (gate) { var result = pending; pending = null; return disposed ? null : result; }
    }

    private bool PublishWindow(nint hwnd)
    {
        lock (gate)
        {
            if (disposed) return false;
            window = hwnd; return true;
        }
    }

    private bool RetireIfIdle()
    {
        lock (gate)
        {
            if (!disposed && pending is not null) return false;
            if (worker == Thread.CurrentThread) { window = 0; worker = null; }
            return true;
        }
    }

    public void Dispose()
    {
        Thread? join; nint target;
        lock (gate) { disposed = true; pending = null; target = window; join = worker; }
        if (target != 0) Win32.PostMessageW(target, StopMessage, 0, 0);
        if (join is not null && join != Thread.CurrentThread) join.Join(400);
    }

    private sealed class Host
    {
        private const string ClassName = "LangSleeper.Indicator.CodeOnly.v2";
        private static readonly object ClassGate = new();
        private static bool classRegistered;
        private static readonly ConcurrentDictionary<nint, Host> Hosts = new();
        private static readonly Win32.WindowProc Callback = Route;
        private readonly IndicatorWindow owner;
        private readonly Stopwatch clock = new();
        private nint hwnd;
        private IndicatorFrame? frame;
        private DisplayRequest? request;
        private int x, y;
        private bool shown;

        internal Host(IndicatorWindow owner) { this.owner = owner; }

        internal void Run()
        {
            lock (ClassGate)
            {
                if (!classRegistered)
                {
                    var wc = new Win32.WNDCLASSEX
                    {
                        Size = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(), ClassName = ClassName,
                        Proc = Marshal.GetFunctionPointerForDelegate(Callback), Instance = Win32.Instance,
                        Cursor = Win32.LoadCursorW(0, 32512)
                    };
                    if (Win32.RegisterClassExW(ref wc) == 0) throw new InvalidOperationException("Could not create the language indicator.");
                    classRegistered = true;
                }
            }
            // No WS_EX_LAYERED: it would replace the compositor's Acrylic with a flat bitmap.
            hwnd = Win32.CreateWindowExW(Win32.EX_TOOLWINDOW | Win32.EX_NOACTIVATE | Win32.EX_TOPMOST | Win32.EX_TRANSPARENT,
                ClassName, "LangSleeper", Win32.WS_POPUP, 0, 0, 1, 1, 0, 0, Win32.Instance, 0);
            if (hwnd == 0) throw new InvalidOperationException("Could not create the language indicator.");
            Hosts.TryAdd(hwnd, this);
            try
            {
                if (!owner.PublishWindow(hwnd)) return;
                PresentPending();
                while (Win32.GetMessageW(out var message, 0, 0, 0) > 0)
                { Win32.TranslateMessage(ref message); Win32.DispatchMessageW(ref message); }
            }
            finally
            {
                frame?.Dispose(); frame = null;
                if (hwnd != 0) { Hosts.TryRemove(hwnd, out _); Win32.DestroyWindow(hwnd); hwnd = 0; }
            }
        }

        private static nint Route(nint window, uint message, nuint wParam, nint lParam)
        {
            if (!Hosts.TryGetValue(window, out var host)) return Win32.DefWindowProcW(window, message, wParam, lParam);
            try { return host.Process(message, wParam, lParam); }
            catch (Exception ex)
            {
                Volatile.Write(ref host.owner.lastError, ex.Message);
                host.Close(); return 0;
            }
        }

        private bool ConfigureBackdrop(DisplayRequest next)
        {
            var dark = next.Dark ? 1 : 0; Win32.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
            var corner = 2; Win32.DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
            var border = unchecked((int)0xFFFFFFFE); Win32.DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            var glass = false;
            if (next.Transparent)
            {
                if (Environment.OSVersion.Version.Build >= 22621)
                {
                    var acrylic = 3;
                    glass = Win32.DwmSetWindowAttribute(hwnd, 38, ref acrylic, 4) == 0;
                }
                else glass = Win32.TrySetAcrylic(hwnd, next.Dark);
            }
            if (!glass)
            {
                var none = 1; Win32.DwmSetWindowAttribute(hwnd, 38, ref none, 4);
                Win32.TrySetAcrylic(hwnd, next.Dark, false);
            }
            var margins = new Win32.MARGINS { Left = glass ? -1 : 0 };
            Win32.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            return glass;
        }

        private void PresentPending()
        {
            var next = owner.TakePending();
            if (next is null) { if (!shown && owner.RetireIfIdle()) Close(); return; }
            request = next;
            var glass = ConfigureBackdrop(next);
            frame?.Dispose(); frame = new IndicatorFrame(next.Code, next.Dark, next.Scale, glass);
            x = next.WorkArea.Left + (next.WorkArea.Width - frame.Width) / 2;
            y = next.WorkArea.Bottom - frame.Height - (int)Math.Round(72 * next.Scale);
            Win32.SetWindowPos(hwnd, -1, x, y, frame.Width, frame.Height, 0x10);
            Win32.InvalidateRect(hwnd, 0, false);
            Volatile.Write(ref owner.currentCode, next.Code);
            clock.Restart();
            if (!shown)
            {
                Volatile.Write(ref owner.visible, 1);
                if (!next.Animate || !IndicatorNative.AnimateWindow(hwnd, 130, 0x80000)) Win32.ShowWindow(hwnd, 4);
                shown = true;
            }
            else Win32.UpdateWindow(hwnd);
            Win32.SetTimer(hwnd, 1, next.Animate ? 16u : (uint)next.Duration, 0);
        }

        private void Tick()
        {
            if (request is not { } current || frame is null) return;
            var elapsed = clock.Elapsed.TotalMilliseconds;
            if (elapsed < current.Duration)
            {
                // A tiny eased settling motion, while the compositor performs the fade.
                if (current.Animate && elapsed < 260)
                {
                    var offset = (int)Math.Round(4 * current.Scale * Math.Exp(-elapsed / 70));
                    Win32.SetWindowPos(hwnd, -1, x, y + offset, frame.Width, frame.Height, 0x10);
                }
                return;
            }
            Win32.KillTimer(hwnd, 1);
            if (!current.Animate || !IndicatorNative.AnimateWindow(hwnd, 130, 0x90000)) Win32.ShowWindow(hwnd, 0);
            shown = false; Volatile.Write(ref owner.visible, 0); clock.Stop();
            if (owner.RetireIfIdle()) Close(); else PresentPending();
        }

        private void Paint(nint dc) { frame?.Present(dc); }
        private void Close()
        {
            if (hwnd == 0) return;
            Win32.KillTimer(hwnd, 1); Win32.ShowWindow(hwnd, 0);
            Volatile.Write(ref owner.visible, 0); shown = false;
            Hosts.TryRemove(hwnd, out _); Win32.DestroyWindow(hwnd); hwnd = 0; Win32.PostQuitMessage(0);
        }
        private nint Process(uint message, nuint wParam, nint lParam)
        {
            switch (message)
            {
                case PresentMessage: PresentPending(); return 0;
                case StopMessage: Close(); return 0;
                case 0x21: return 3; // MA_NOACTIVATE
                case 0x84: return -1; // HTTRANSPARENT
                case Win32.WM_ERASEBKGND: return 1;
                case 0x318: Paint((nint)wParam); return 0; // WM_PRINTCLIENT, required by AnimateWindow
                case Win32.WM_PAINT:
                    var dc = Win32.BeginPaint(hwnd, out var paint);
                    try { Paint(dc); } finally { Win32.EndPaint(hwnd, ref paint); }
                    return 0;
                case Win32.WM_TIMER: Tick(); return 0;
            }
            return Win32.DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }
}
