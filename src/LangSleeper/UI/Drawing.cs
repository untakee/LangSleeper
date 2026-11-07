using LangSleeper.Native;
using Microsoft.Win32;

namespace LangSleeper.UI;

internal sealed class Palette
{
    internal bool Dark { get; }
    internal uint Background, Surface, Raised, Border, Text, Muted, Accent, AccentSoft, Green, Error;
    internal uint PrimaryText => Dark ? Hex(0x121416) : Hex(0xFFFFFF);
    internal Palette(bool dark)
    {
        Dark = dark;
        Background = Hex(dark ? 0x141618 : 0xF0F2F3);
        Surface = Hex(dark ? 0x24272A : 0xFAFBFC);
        Raised = Hex(dark ? 0x303437 : 0xFFFFFF);
        Border = Hex(dark ? 0x464B50 : 0xD8DDE0);
        Text = Hex(dark ? 0xF7F8F9 : 0x202427);
        Muted = Hex(dark ? 0xAFB6BC : 0x667078);
        Accent = Hex(dark ? 0xF4F6F7 : 0x202427);
        AccentSoft = Hex(dark ? 0x383D41 : 0xE9EDEF);
        Green = Hex(dark ? 0xA2D8B7 : 0x28794F);
        Error = Hex(dark ? 0xFFABA8 : 0xB33532);
    }
    internal static uint Hex(int rgb) => Hex(unchecked((uint)rgb));
    internal static uint Hex(uint rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | (rgb >> 16);
    internal static uint Argb(uint color, byte alpha = 255) => ((uint)alpha << 24) | Hex(color);
    internal static bool SystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
    internal static bool TransparencyEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("EnableTransparency") is not int value || value != 0;
    }
    internal static bool AnimationsEnabled => Win32.SystemParametersInfoW(0x1042, 0, out var value, 0) && value != 0;
}

internal sealed class Drawing : IDisposable
{
    private readonly Dictionary<(int Size, int Weight), nint> fonts = [], alphaFonts = [];
    private readonly Dictionary<int, nint> families = [];
    private readonly Dictionary<bool, nint> formats = [];
    private bool alphaInitialized;
    internal double Scale { get; set; }
    internal double Opacity { get; set; } = 1;
    internal Drawing(double scale) { Scale = scale; }
    internal int S(double value) => (int)Math.Round(value * Scale);
    private uint Alpha(uint argb) => (argb & 0xFFFFFF) | ((uint)Math.Round((argb >> 24) * Math.Clamp(Opacity, 0, 1)) << 24);
    internal void Offset(nint dc, double x, double y, Action action)
    {
        if (!PaintSurface.GraphicsByDc.TryGetValue(dc, out var graphics)) { action(); return; }
        GdiPlus.GdipSaveGraphics(graphics, out var state);
        try { GdiPlus.GdipTranslateWorldTransform(graphics, S(x), S(y), 0); action(); }
        finally { GdiPlus.GdipRestoreGraphics(graphics, state); }
    }
    internal static uint Blend(uint first, uint second, double progress)
    {
        uint channel(int shift) => (uint)Math.Round(((first >> shift) & 255) * (1 - progress) + ((second >> shift) & 255) * progress);
        return channel(0) | (channel(8) << 8) | (channel(16) << 16);
    }
    internal nint Font(int size = 13, int weight = 400)
    {
        var key = (S(size), weight);
        if (!fonts.TryGetValue(key, out var font))
        {
            font = Win32.CreateFontW(-key.Item1, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            fonts.Add(key, font);
        }
        return font;
    }
    private nint AlphaFont(int size, int weight)
    {
        if (!alphaInitialized) { GdiPlusSession.Acquire(); alphaInitialized = true; }
        var key = (S(size), weight);
        if (alphaFonts.TryGetValue(key, out var font)) return font;
        if (!families.TryGetValue(weight, out var family))
        {
            GdiPlus.GdipCreateFontFamilyFromName(weight >= 600 ? "Segoe UI Semibold" : "Segoe UI", 0, out family);
            families.Add(weight, family);
        }
        GdiPlus.GdipCreateFont(family, S(size), 0, 2, out font); alphaFonts.Add(key, font); return font;
    }
    internal void Text(nint dc, string text, int x, int y, int width, int height, uint color, int size = 13, int weight = 400, uint format = 0)
    {
        if (PaintSurface.GraphicsByDc.TryGetValue(dc, out var graphics))
        {
            var font = AlphaFont(size, weight); var center = (format & Win32.DT_CENTER) != 0;
            if (!formats.TryGetValue(center, out var stringFormat))
            {
                GdiPlus.GdipCreateStringFormat(0x1000, 0, out stringFormat);
                GdiPlus.GdipSetStringFormatAlign(stringFormat, center ? 1 : 0);
                GdiPlus.GdipSetStringFormatLineAlign(stringFormat, 1);
                GdiPlus.GdipSetStringFormatTrimming(stringFormat, 3); formats.Add(center, stringFormat);
            }
            GdiPlus.GdipCreateSolidFill(Alpha(Palette.Argb(color)), out var brush);
            var bounds = new GdiPlus.RectF(S(x), S(y), S(width), S(height));
            GdiPlus.GdipDrawString(graphics, text, text.Length, font, ref bounds, stringFormat, brush);
            GdiPlus.GdipDeleteBrush(brush); return;
        }
        var old = Win32.SelectObject(dc, Font(size, weight));
        Win32.SetBkMode(dc, 1); Win32.SetTextColor(dc, color);
        var rect = new Win32.RECT(S(x), S(y), S(width), S(height));
        Win32.DrawTextW(dc, text, text.Length, ref rect, format | Win32.DT_SINGLELINE | Win32.DT_VCENTER | Win32.DT_END_ELLIPSIS | Win32.DT_NOPREFIX);
        Win32.SelectObject(dc, old);
    }
    internal void Box(nint dc, int x, int y, int width, int height, int radius, uint fill, uint? border = null)
    {
        if (PaintSurface.GraphicsByDc.ContainsKey(dc))
        { GlassBox(dc, x, y, width, height, radius, Palette.Argb(fill), border.HasValue ? Palette.Argb(border.Value) : null); return; }
        var brush = Win32.CreateSolidBrush(fill);
        var pen = border.HasValue ? Win32.CreatePen(0, 1, border.Value) : Win32.GetStockObject(8);
        var oldBrush = Win32.SelectObject(dc, brush); var oldPen = Win32.SelectObject(dc, pen);
        Win32.RoundRect(dc, S(x), S(y), S(x + width), S(y + height), S(radius * 2), S(radius * 2));
        Win32.SelectObject(dc, oldPen); Win32.SelectObject(dc, oldBrush);
        Win32.DeleteObject(brush); if (border.HasValue) Win32.DeleteObject(pen);
    }
    internal void GlassBox(nint dc, int x, int y, int width, int height, int radius, uint argb, uint? border = null, uint? bottom = null)
    {
        if (!PaintSurface.GraphicsByDc.TryGetValue(dc, out var graphics)) return;
        nint brush; var bounds = new GdiPlus.RectF(S(x), S(y), S(width), S(height));
        if (bottom.HasValue) GdiPlus.GdipCreateLineBrushFromRect(ref bounds, Alpha(argb), Alpha(bottom.Value), 1, 0, out brush);
        else GdiPlus.GdipCreateSolidFill(Alpha(argb), out brush);
        if (radius == 0) GdiPlus.GdipFillRectangle(graphics, brush, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        else
        {
            var path = RoundedPath(x + 0.5f, y + 0.5f, width - 1, height - 1, radius);
            GdiPlus.GdipFillPath(graphics, brush, path);
            if (border.HasValue)
            {
                GdiPlus.GdipCreatePen1(Alpha(border.Value), (float)Scale, 2, out var pen);
                GdiPlus.GdipDrawPath(graphics, pen, path); GdiPlus.GdipDeletePen(pen);
            }
            GdiPlus.GdipDeletePath(path);
        }
        GdiPlus.GdipDeleteBrush(brush);
    }
    private nint RoundedPath(float x, float y, float width, float height, float radius)
    {
        GdiPlus.GdipCreatePath(0, out var path); var r = (float)(radius * 2 * Scale);
        var px = (float)(x * Scale); var py = (float)(y * Scale); var w = (float)(width * Scale); var h = (float)(height * Scale);
        GdiPlus.GdipAddPathArc(path, px, py, r, r, 180, 90); GdiPlus.GdipAddPathArc(path, px + w - r, py, r, r, 270, 90);
        GdiPlus.GdipAddPathArc(path, px + w - r, py + h - r, r, r, 0, 90); GdiPlus.GdipAddPathArc(path, px, py + h - r, r, r, 90, 90);
        GdiPlus.GdipClosePathFigure(path); return path;
    }
    internal void Line(nint dc, int x, int y, int x2, int y2, uint color)
    {
        if (PaintSurface.GraphicsByDc.TryGetValue(dc, out var graphics))
        {
            GdiPlus.GdipCreatePen1(Alpha(Palette.Argb(color)), Math.Max(1, (float)Scale), 2, out var linePen);
            GdiPlus.GdipDrawLine(graphics, linePen, S(x), S(y), S(x2), S(y2)); GdiPlus.GdipDeletePen(linePen); return;
        }
        var pen = Win32.CreatePen(0, 1, color); var old = Win32.SelectObject(dc, pen);
        Win32.MoveToEx(dc, S(x), S(y), 0); Win32.LineTo(dc, S(x2), S(y2));
        Win32.SelectObject(dc, old); Win32.DeleteObject(pen);
    }
    internal void Moon(nint dc, int x, int y, int size, uint color, uint background)
    { Circle(dc, x, y, size, color); Circle(dc, x + size / 3, y - size / 6, size * 4 / 5, background); }
    internal void Circle(nint dc, int x, int y, int size, uint fill)
    {
        if (PaintSurface.GraphicsByDc.TryGetValue(dc, out var graphics))
        {
            GdiPlus.GdipCreateSolidFill(Alpha(Palette.Argb(fill)), out var fillBrush);
            GdiPlus.GdipFillEllipse(graphics, fillBrush, S(x), S(y), S(size), S(size)); GdiPlus.GdipDeleteBrush(fillBrush); return;
        }
        var brush = Win32.CreateSolidBrush(fill); var old = Win32.SelectObject(dc, brush); var pen = Win32.SelectObject(dc, Win32.GetStockObject(8));
        Win32.Ellipse(dc, S(x), S(y), S(x + size), S(y + size));
        Win32.SelectObject(dc, old); Win32.SelectObject(dc, pen); Win32.DeleteObject(brush);
    }
    public void Dispose()
    {
        foreach (var font in fonts.Values) Win32.DeleteObject(font); fonts.Clear();
        foreach (var font in alphaFonts.Values) GdiPlus.GdipDeleteFont(font); alphaFonts.Clear();
        foreach (var family in families.Values) GdiPlus.GdipDeleteFontFamily(family); families.Clear();
        foreach (var format in formats.Values) GdiPlus.GdipDeleteStringFormat(format); formats.Clear();
        if (alphaInitialized) { GdiPlusSession.Release(); alphaInitialized = false; }
    }
}

internal static class Backdrop
{
    internal static bool Apply(nint window, bool dark, bool popup = false)
    {
        var value = dark ? 1 : 0; Win32.DwmSetWindowAttribute(window, 20, ref value, 4);
        value = 2; Win32.DwmSetWindowAttribute(window, 33, ref value, 4);
        value = unchecked((int)0xFFFFFFFE); Win32.DwmSetWindowAttribute(window, 34, ref value, 4);
        var active = false;
        if (Palette.TransparencyEnabled())
        {
            if (Environment.OSVersion.Version.Build >= 22621)
            {
                value = 3; // Desktop Acrylic samples and blurs the windows behind this one.
                active = Win32.DwmSetWindowAttribute(window, 38, ref value, 4) == 0;
            }
            else active = Win32.TrySetAcrylic(window, dark);
        }
        if (!active)
        {
            value = 1; Win32.DwmSetWindowAttribute(window, 38, ref value, 4);
            Win32.TrySetAcrylic(window, dark, false);
        }
        var margins = new Win32.MARGINS { Left = active ? -1 : 0 };
        Win32.DwmExtendFrameIntoClientArea(window, ref margins);
        return active;
    }
}
