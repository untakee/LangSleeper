using System.Runtime.InteropServices;

namespace LangSleeper.Native;

internal static class LayoutNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadKeyboardLayoutW(string layoutId, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint ActivateKeyboardLayout(nint layout, uint flags);
}
