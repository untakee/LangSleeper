using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LangSleeper.Native;

namespace LangSleeper;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowW(string className, string title);

    [STAThread]
    private static int Main(string[] args)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var directory = Option(args, "--config-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LangSleeper");
            directory = Path.GetFullPath(directory);
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant())))[..16];
            var identity = "LangSleeper." + suffix;
            using var mutex = new Mutex(true, @"Local\" + identity, out var created);
            if (!created || args.Contains("--exit"))
            {
                var existing = FindWindowW("LangSleeper.Host", identity);
                if (existing != 0) Win32.PostMessageW(existing, args.Contains("--exit") ? Win32.WM_CLOSE : App.OpenSettingsMessage, 0, 0);
                return 0;
            }
            var init = new Win32.INITCOMMONCONTROLSEX { Size = 8, Classes = 0x4000 | 0x40 };
            Win32.InitCommonControlsEx(ref init);
            using var app = new App(directory, identity, !args.Contains("--no-autostart"), Option(args, "--diagnostics"), clock);
            if (args.Contains("--settings")) app.OpenSettings();
            int result;
            while ((result = Win32.GetMessageW(out var message, 0, 0, 0)) > 0)
            {
                if (app.SettingsHandle != 0 && Win32.IsDialogMessageW(app.SettingsHandle, ref message)) continue;
                Win32.TranslateMessage(ref message); Win32.DispatchMessageW(ref message);
            }
            if (result < 0) return 1;
            return 0;
        }
        catch (Exception ex)
        {
            Win32.MessageBoxW(0, ex.Message, "LangSleeper could not start", 0x10); return 1;
        }
    }
    private static string? Option(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} requires a value.");
        return args[index + 1];
    }
}
