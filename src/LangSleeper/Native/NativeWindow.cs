using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LangSleeper.Native;

internal abstract class NativeWindow : IDisposable
{
    private static readonly Dictionary<nint, NativeWindow> Windows = [];
    private static readonly HashSet<string> Classes = [];
    private static readonly Win32.WindowProc Callback = Route;
    internal nint Handle { get; private set; }

    protected void Create(string className, string title, uint style, uint extended, int x, int y, int width, int height, nint owner = 0)
    {
        if (Classes.Add(className))
        {
            var wc = new Win32.WNDCLASSEX
            {
                Size = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(), ClassName = className, Style = 0x20000,
                Proc = Marshal.GetFunctionPointerForDelegate(Callback), Instance = Win32.Instance,
                Cursor = Win32.LoadCursorW(0, 32512), Icon = Win32.LoadIconW(Win32.Instance, 32512)
            };
            if (Win32.RegisterClassExW(ref wc) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        Handle = Win32.CreateWindowExW(extended, className, title, style, x, y, width, height, owner, 0, Win32.Instance, 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        Windows.Add(Handle, this);
    }

    private static nint Route(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (!Windows.TryGetValue(hwnd, out var window)) return Win32.DefWindowProcW(hwnd, message, wParam, lParam);
        try { return window.ProcessMessage(message, wParam, lParam); }
        catch (Exception ex)
        {
            // Exceptions must never unwind through an unmanaged window procedure.
            Win32.MessageBoxW(hwnd, ex.Message, "LangSleeper", 0x10);
            return 0;
        }
        finally
        {
            if (message == 0x82)
            {
                Windows.Remove(hwnd);
                window.Handle = 0;
                window.OnDestroyed();
            }
        }
    }

    protected virtual nint ProcessMessage(uint message, nuint wParam, nint lParam) => Win32.DefWindowProcW(Handle, message, wParam, lParam);
    protected virtual void OnDestroyed() { }
    public virtual void Dispose() { if (Handle != 0) Win32.DestroyWindow(Handle); GC.SuppressFinalize(this); }
}
