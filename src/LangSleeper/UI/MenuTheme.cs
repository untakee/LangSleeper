using System.Runtime.InteropServices;
using LangSleeper.Native;

namespace LangSleeper.UI;

internal static class MenuTheme
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMode(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void FlushThemes();
    // The optional Windows theme entry points are version-guarded. Standard menus remain usable if unavailable.
    internal static void Apply(bool dark)
    {
        if (Environment.OSVersion.Version.Build < 18362) return;
        var module = Win32.GetModuleHandleW("uxtheme.dll");
        if (module == 0) return;
        var setMode = Win32.GetProcAddress(module, 135); var flush = Win32.GetProcAddress(module, 136);
        if (setMode != 0) Marshal.GetDelegateForFunctionPointer<SetMode>(setMode)(dark ? 2 : 3);
        if (flush != 0) Marshal.GetDelegateForFunctionPointer<FlushThemes>(flush)();
    }
}
