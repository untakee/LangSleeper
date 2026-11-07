using LangSleeper.Native;

namespace LangSleeper.UI;

/// <summary>A premultiplied-alpha surface. GDI text alone would erase alpha on a glass window.</summary>
internal sealed class PaintSurface : IDisposable
{
    internal static readonly Dictionary<nint, nint> GraphicsByDc = [];
    private readonly nint dc, bitmap, previous, image, graphics;
    private bool disposed;
    internal nint Dc => dc;
    internal nint Pixels { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal PaintSurface(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var stride = checked(width * 4);
        if ((long)stride * height > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(height), "The paint surface is too large.");
        GdiPlusSession.Acquire();
        Width = width; Height = height;
        try
        {
            dc = Win32.CreateCompatibleDC(0);
            if (dc == 0) throw new InvalidOperationException("Windows could not create the paint device context.");
            var info = new Win32.BITMAPINFO { Header = new() { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 } };
            bitmap = Win32.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0); Pixels = bits;
            if (bitmap == 0 || bits == 0) throw new InvalidOperationException("Windows could not create the glass surface.");
            previous = Win32.SelectObject(dc, bitmap);
            if (previous == 0 || previous == -1) throw new InvalidOperationException("Windows could not select the paint bitmap.");
            Check(GdiPlus.GdipCreateBitmapFromScan0(width, height, stride, 0xE200B, bits, out image), "Create bitmap");
            Check(GdiPlus.GdipGetImageGraphicsContext(image, out graphics), "Create graphics context");
            Check(GdiPlus.GdipSetSmoothingMode(graphics, 4), "Set antialiasing");
            Check(GdiPlus.GdipSetTextRenderingHint(graphics, 4), "Set alpha-preserving text rendering");
            Check(GdiPlus.GdipSetPixelOffsetMode(graphics, 4), "Set pixel alignment");
            Check(GdiPlus.GdipGraphicsClear(graphics, 0), "Clear paint surface");
            GraphicsByDc.Add(dc, graphics);
        }
        catch
        {
            ReleaseResources();
            throw;
        }
    }
    internal void Flush()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Check(GdiPlus.GdipFlush(graphics, 1), "Flush paint surface");
    }
    internal void Present(nint target, int x = 0, int y = 0)
    {
        if (target == 0) throw new ArgumentException("The target device context must be valid.", nameof(target));
        Flush();
        if (!Win32.BitBlt(target, x, y, Width, Height, dc, 0, 0, 0xCC0020))
            throw new InvalidOperationException("Windows could not present the paint surface.");
    }
    internal void SavePng(string absolutePath)
    {
        Flush();
        ImageExport.SavePng(image, absolutePath);
    }
    private static void Check(int status, string operation)
    { if (status != 0) throw new InvalidOperationException($"{operation} failed (GDI+ status {status})."); }
    private void ReleaseResources()
    {
        GraphicsByDc.Remove(dc);
        if (graphics != 0) GdiPlus.GdipDeleteGraphics(graphics);
        if (image != 0) GdiPlus.GdipDisposeImage(image);
        if (previous != 0 && previous != -1 && dc != 0) Win32.SelectObject(dc, previous);
        if (bitmap != 0) Win32.DeleteObject(bitmap);
        if (dc != 0) Win32.DeleteDC(dc);
        GdiPlusSession.Release();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ReleaseResources();
    }
}
