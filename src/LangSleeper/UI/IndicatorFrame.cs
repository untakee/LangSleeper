using LangSleeper.Native;

namespace LangSleeper.UI;

/// <summary>Independent renderer: indicator's UI thread never uses the settings renderer's mutable caches.</summary>
internal sealed class IndicatorFrame : IDisposable
{
    private nuint token;
    private readonly nint dc, bitmap, previous, image, graphics, family, font, format;
    internal int Width { get; }
    internal int Height { get; }
    internal nint Pixels { get; }
    internal string Code { get; }

    internal static string NormalizeCode(string? value)
    {
        var code = value?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(code) ? "??" : code.Length <= 4 ? code : code[..4];
    }

    internal IndicatorFrame(string code, bool dark, double scale = 1, bool glass = true)
    {
        Code = NormalizeCode(code);
        scale = Math.Clamp(scale, 0.75, 4);
        Width = (int)Math.Round((Code.Length > 2 ? 96 : 80) * scale); Height = (int)Math.Round(50 * scale);
        var input = new GdiPlus.StartupInput { Version = 1 };
        if (GdiPlus.GdiplusStartup(out token, ref input, 0) != 0) throw new InvalidOperationException("Could not initialize the language indicator renderer.");
        dc = Win32.CreateCompatibleDC(0);
        var info = new Win32.BITMAPINFO { Header = new() { Size = 40, Width = Width, Height = -Height, Planes = 1, BitCount = 32 } };
        bitmap = Win32.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0); Pixels = bits;
        if (bitmap == 0) { Win32.DeleteDC(dc); GdiPlus.GdiplusShutdown(token); token = 0; throw new InvalidOperationException("Could not create the language indicator surface."); }
        previous = Win32.SelectObject(dc, bitmap);
        GdiPlus.GdipCreateBitmapFromScan0(Width, Height, Width * 4, 0xE200B, bits, out image);
        GdiPlus.GdipGetImageGraphicsContext(image, out graphics);
        GdiPlus.GdipSetSmoothingMode(graphics, 4); GdiPlus.GdipSetTextRenderingHint(graphics, 4);
        // The native compositor supplies live Desktop Acrylic; this is only the neutral tint above it.
        GdiPlus.GdipGraphicsClear(graphics, glass ? dark ? 0xAA191B1Du : 0xA0FFFFFFu : dark ? 0xFF202326u : 0xFFF9FAFBu);
        var rect = new GdiPlus.RectF(0, 0, Width, Height);
        GdiPlus.GdipCreateLineBrushFromRect(ref rect, dark ? 0x0CFFFFFFu : 0x1AFFFFFFu, 0x00FFFFFF, 1, 0, out var sheen);
        GdiPlus.GdipFillRectangle(graphics, sheen, 0, 0, Width, Height); GdiPlus.GdipDeleteBrush(sheen);
        GdiPlus.GdipCreateFontFamilyFromName("Segoe UI Semibold", 0, out family);
        GdiPlus.GdipCreateFont(family, (float)(20 * scale), 0, 2, out font);
        GdiPlus.GdipCreateStringFormat(0x1000, 0, out format);
        GdiPlus.GdipSetStringFormatAlign(format, 1); GdiPlus.GdipSetStringFormatLineAlign(format, 1);
        GdiPlus.GdipCreateSolidFill(dark ? 0xFFF5F7F8 : 0xFF23272A, out var text);
        GdiPlus.GdipDrawString(graphics, Code, Code.Length, font, ref rect, format, text); GdiPlus.GdipDeleteBrush(text);
        GdiPlus.GdipFlush(graphics, 1);
    }

    internal void Present(nint target) => Win32.BitBlt(target, 0, 0, Width, Height, dc, 0, 0, 0xCC0020);

    // A deterministic export of the exact code/tint surface, without a screenshot or desktop sampling.
    internal static void RenderToPng(string path, string code, bool dark, double scale = 1)
    {
        using var frame = new IndicatorFrame(code, dark, scale);
        var encoder = new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e");
        if (IndicatorNative.GdipSaveImageToFile(frame.image, Path.GetFullPath(path), ref encoder, 0) != 0)
            throw new IOException("Could not export the indicator preview.");
    }

    public void Dispose()
    {
        if (token == 0) return;
        GdiPlus.GdipDeleteStringFormat(format); GdiPlus.GdipDeleteFont(font); GdiPlus.GdipDeleteFontFamily(family);
        GdiPlus.GdipDeleteGraphics(graphics); GdiPlus.GdipDisposeImage(image);
        Win32.SelectObject(dc, previous); Win32.DeleteObject(bitmap); Win32.DeleteDC(dc);
        GdiPlus.GdiplusShutdown(token); token = 0;
    }
}
