using System.Runtime.InteropServices;

namespace LangSleeper.Native;

internal static class ImageExport
{
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GdipSaveImageToFile(nint image, string filename, ref Guid encoder, nint parameters);

    internal static void SavePng(nint image, string absolutePath)
    {
        if (image == 0) throw new ArgumentException("The image handle must be valid.", nameof(image));
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
            throw new ArgumentException("PNG export requires an absolute path.", nameof(absolutePath));

        var path = Path.GetFullPath(absolutePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new Guid("557CF406-1A04-11D3-9A73-0000F81EF32E");
        var status = GdipSaveImageToFile(image, path, ref encoder, 0);
        if (status != 0) throw new IOException($"Could not export the PNG image (GDI+ status {status}): {path}");
    }
}
