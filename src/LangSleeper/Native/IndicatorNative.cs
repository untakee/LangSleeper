using System.Runtime.InteropServices;

namespace LangSleeper.Native;

internal static class IndicatorNative
{
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AnimateWindow(nint window, uint milliseconds, uint flags);

    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int GdipSaveImageToFile(nint image, string path, ref Guid encoder, nint parameters);
}
