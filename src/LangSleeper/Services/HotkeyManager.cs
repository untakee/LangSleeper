using System.Runtime.InteropServices;
using LangSleeper.Core;
using LangSleeper.Native;

namespace LangSleeper.Services;

internal sealed class HotkeyManager(nint window) : IDisposable
{
    private readonly Dictionary<Hotkey, int> registered = [];
    private Dictionary<int, string?> actions = [];
    private int nextId = 1;
    internal bool TryGetAction(int id, out string? layout) => actions.TryGetValue(id, out layout);

    // Keep old registrations until every new shortcut succeeds: a failed Apply does not break the running setup.
    internal bool TryApply(Configuration config, out string? error)
    {
        var desired = new Dictionary<Hotkey, string?> { [config.CycleHotkey] = null };
        foreach (var extra in config.ExtraLayouts) desired.Add(extra.Hotkey, extra.LayoutId);
        var added = new List<Hotkey>();
        foreach (var hotkey in desired.Keys)
        {
            if (registered.ContainsKey(hotkey)) continue;
            if (nextId >= 0xBFFF) nextId = 1;
            while (registered.ContainsValue(nextId)) nextId++;
            var id = nextId++;
            if (!Win32.RegisterHotKey(window, id, hotkey.Modifiers | 0x4000, hotkey.Key))
            {
                var code = Marshal.GetLastWin32Error();
                foreach (var key in added) { Win32.UnregisterHotKey(window, registered[key]); registered.Remove(key); }
                error = $"{hotkey} is unavailable (Windows error {code}). Choose another shortcut.";
                return false;
            }
            registered.Add(hotkey, id);
            added.Add(hotkey);
        }
        foreach (var old in registered.Keys.Where(x => !desired.ContainsKey(x)).ToList())
        { Win32.UnregisterHotKey(window, registered[old]); registered.Remove(old); }
        actions = desired.ToDictionary(x => registered[x.Key], x => x.Value);
        error = null;
        return true;
    }

    public void Dispose()
    {
        foreach (var id in registered.Values) Win32.UnregisterHotKey(window, id);
        registered.Clear(); actions.Clear();
    }
}
