using System.Runtime.InteropServices;
using LangSleeper.Native;

namespace LangSleeper.UI;

internal sealed class TrayIcon(nint window) : IDisposable
{
    internal const uint CallbackMessage = Win32.WM_APP + 1;
    private nint icon;
    private bool added;
    private string lastCode = "";
    private string lastLanguage = "system";
    private bool lastPaused, lastDark;
    private Win32.NOTIFYICONDATA Data => new()
    {
        Size = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATA>(), Hwnd = window, Id = 1,
        Flags = 1 | 2 | 4, CallbackMessage = CallbackMessage, Icon = icon,
        Tip = "LangSleeper", Info = "", InfoTitle = ""
    };

    internal void Update(string code, bool paused, bool dark, bool force = false, string language = "system")
    {
        if (!force && code == lastCode && paused == lastPaused && dark == lastDark && language == lastLanguage && added) return;
        lastCode = code; lastPaused = paused; lastDark = dark; lastLanguage = language;
        var replacement = MakeIcon(paused ? "Ⅱ" : code, dark);
        var previous = icon; icon = replacement;
        var data = Data;
        data.Tip = $"LangSleeper · {code}{(paused ? UiText.Get(language, " · Paused", " · Пауза") : "")}\n{UiText.Get(language, "Double-click for settings", "Двойной щелчок — настройки")}";
        added = Win32.Shell_NotifyIconW(added ? 1u : 0u, ref data);
        if (previous != 0) Win32.DestroyIcon(previous);
    }
    internal void Recreate() { added = false; Update(lastCode, lastPaused, lastDark, true, lastLanguage); }
    internal void Notify(string title, string message, bool error = false)
    {
        var data = Data; data.Flags = 16; data.InfoTitle = title; data.Info = message.Length > 255 ? message[..255] : message;
        data.InfoFlags = error ? 3u : 1u; Win32.Shell_NotifyIconW(1, ref data);
    }
    internal static unsafe nint MakeIcon(string code, bool dark)
    {
        const int size = 32;
        var dc = Win32.CreateCompatibleDC(0);
        var info = new Win32.BITMAPINFO { Header = new() { Size = 40, Width = size, Height = -size, Planes = 1, BitCount = 32 } };
        var bitmap = Win32.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
        new Span<byte>((void*)bits, size * size * 4).Clear();
        var old = Win32.SelectObject(dc, bitmap);
        using (var draw = new Drawing(1))
        {
            var palette = new Palette(dark);
            draw.Box(dc, 0, 0, size, size, 8, palette.AccentSoft);
            draw.Text(dc, code, 0, 0, size, size - 1, palette.Accent, code.Length > 2 ? 13 : 15, 700, Win32.DT_CENTER);
        }
        // GDI text drawing clears alpha. Reconstruct alpha only inside the rounded badge.
        var pixels = (uint*)bits;
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var dx = Math.Max(7 - x, x - 24); var dy = Math.Max(7 - y, y - 24);
            if (dx <= 0 || dy <= 0 || dx * dx + dy * dy <= 49) pixels[y * size + x] |= 0xFF000000;
        }
        var mask = Win32.CreateBitmap(size, size, 1, 1, 0);
        var iconInfo = new Win32.ICONINFO { IsIcon = 1, Mask = mask, Color = bitmap };
        var result = Win32.CreateIconIndirect(ref iconInfo);
        Win32.SelectObject(dc, old); Win32.DeleteObject(mask); Win32.DeleteObject(bitmap); Win32.DeleteDC(dc);
        return result;
    }
    public void Dispose()
    {
        if (added) { var data = Data; Win32.Shell_NotifyIconW(2, ref data); }
        if (icon != 0) { Win32.DestroyIcon(icon); icon = 0; } added = false;
    }
}
