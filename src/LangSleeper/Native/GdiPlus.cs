using System.Runtime.InteropServices;

namespace LangSleeper.Native;

internal static class GdiPlus
{
    [StructLayout(LayoutKind.Sequential)] internal struct StartupInput { public uint Version; public nint Debug; public int SuppressThread, SuppressCodecs; }
    [StructLayout(LayoutKind.Sequential)] internal struct RectF { public float X, Y, Width, Height; public RectF(float x, float y, float w, float h) { X = x; Y = y; Width = w; Height = h; } }
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdiplusStartup(out nuint token, ref StartupInput input, nint output);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern void GdiplusShutdown(nuint token);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, nint scan0, out nint bitmap);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipGetImageGraphicsContext(nint bitmap, out nint graphics);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipGraphicsClear(nint graphics, uint argb);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetSmoothingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetTextRenderingHint(nint graphics, int hint);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipFlush(nint graphics, int intention);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSaveGraphics(nint graphics, out uint state);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipRestoreGraphics(nint graphics, uint state);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipTranslateWorldTransform(nint graphics, float x, float y, int order);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateSolidFill(uint argb, out nint brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateLineBrushFromRect(ref RectF rect, uint first, uint second, int mode, int wrap, out nint brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipFillRectangle(nint graphics, nint brush, float x, float y, float width, float height);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float width, float height);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreatePen1(uint argb, float width, int unit, out nint pen);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeletePen(nint pen);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDrawLine(nint graphics, nint pen, float x, float y, float x2, float y2);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreatePath(int mode, out nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipClosePathFigure(nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipFillPath(nint graphics, nint brush, nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDrawPath(nint graphics, nint pen, nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeletePath(nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int GdipCreateFontFamilyFromName(string name, nint collection, out nint family);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteFontFamily(nint family);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateFont(nint family, float size, int style, int unit, out nint font);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteFont(nint font);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateStringFormat(int flags, ushort language, out nint format);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetStringFormatAlign(nint format, int align);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetStringFormatLineAlign(nint format, int align);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetStringFormatTrimming(nint format, int trim);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteStringFormat(nint format);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int GdipDrawString(nint graphics, string text, int length, nint font, ref RectF rect, nint format, nint brush);
}
