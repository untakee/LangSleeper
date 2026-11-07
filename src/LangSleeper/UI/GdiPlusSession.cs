using LangSleeper.Native;
namespace LangSleeper.UI;
internal static class GdiPlusSession
{
    private static int users;
    private static nuint token;
    internal static void Acquire()
    {
        if (users++ != 0) return;
        var input = new GdiPlus.StartupInput { Version = 1 };
        if (GdiPlus.GdiplusStartup(out token, ref input, 0) != 0)
        { users = 0; throw new InvalidOperationException("Windows could not initialize the renderer."); }
    }
    internal static void Release() { if (--users == 0) { GdiPlus.GdiplusShutdown(token); token = 0; } }
}
