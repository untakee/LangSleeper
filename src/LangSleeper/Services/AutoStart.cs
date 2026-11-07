using Microsoft.Win32;

namespace LangSleeper.Services;

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("LangSleeper") as string;
    }

    internal static void Set(bool enabled, string? exactValue = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue("LangSleeper", exactValue ?? $"\"{Environment.ProcessPath}\" --background", RegistryValueKind.String);
        else key.DeleteValue("LangSleeper", false);
    }
}
