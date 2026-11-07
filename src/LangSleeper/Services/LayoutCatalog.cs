using System.Globalization;
using System.Text;
using LangSleeper.Core;
using LangSleeper.Native;
using Microsoft.Win32;

namespace LangSleeper.Services;

internal static class LayoutCatalog
{
    private const string LayoutRoot = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";
    internal sealed record Registration(string Klid, ushort LanguageId, ushort? DeviceId, string? LayoutText, nint ProfileHandle = 0);

    // Kept as a raw-handle formatter for diagnostics. Persisted layout IDs are stable Windows KLIDs.
    internal static string Id(nint handle) => unchecked((uint)(long)handle).ToString("X8");
    private static bool TryKlid(string? value, out string klid)
    {
        klid = "";
        if (value?.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number)) return false;
        klid = number.ToString("X8"); return true;
    }
    internal static string? ResolveSubstitution(string id, IReadOnlyDictionary<string, string> substitutions)
    {
        if (!TryKlid(id, out var current)) return null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (substitutions.TryGetValue(current, out var next))
        {
            if (!seen.Add(current) || !TryKlid(next, out current)) return null;
            if (seen.Count >= 32) return null;
        }
        return current;
    }
    private static Dictionary<string, string> ReadSubstitutions()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var key = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Substitutes");
        if (key is null) return result;
        foreach (var name in key.GetValueNames())
            if (TryKlid(name, out var id) && key.GetValue(name) is string value && TryKlid(value, out var replacement)) result[id] = replacement;
        return result;
    }
    private static List<string> ReadConfiguredIds(IReadOnlyDictionary<string, string> substitutions)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            if (value is null) return;
            var id = ResolveSubstitution(value, substitutions);
            if (id is not null && seen.Add(id)) result.Add(id);
        }
        using (var preload = Registry.CurrentUser.OpenSubKey(@"Keyboard Layout\Preload"))
        {
            if (preload is not null)
                foreach (var name in preload.GetValueNames().OrderBy(x => int.TryParse(x, out var n) ? n : int.MaxValue))
                    Add(preload.GetValue(name) as string);
        }
        // Modern Windows may keep a configured keyboard in the language profile before it is loaded.
        using (var profile = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\User Profile"))
        {
            if (profile is not null)
                foreach (var language in profile.GetSubKeyNames())
                {
                    using var key = profile.OpenSubKey(language);
                    if (key is null) continue;
                    foreach (var name in key.GetValueNames())
                    {
                        var separator = name.IndexOf(':');
                        if (separator > 0 && name.Length == separator + 9) Add(name[(separator + 1)..]);
                    }
                }
        }
        return result;
    }
    private static Dictionary<uint, nint> ReadProfileHandles()
    {
        var result = new Dictionary<uint, nint>();
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\CTF\SortOrder\AssemblyItem");
        if (root is null) return result;
        // Read a bounded, known registry hierarchy; never search the whole registry.
        foreach (var language in root.GetSubKeyNames())
        {
            using var languageKey = root.OpenSubKey(language);
            if (languageKey is null) continue;
            foreach (var assembly in languageKey.GetSubKeyNames())
            {
                using var assemblyKey = languageKey.OpenSubKey(assembly);
                if (assemblyKey is null) continue;
                foreach (var item in assemblyKey.GetSubKeyNames())
                {
                    using var itemKey = assemblyKey.OpenSubKey(item);
                    var value = itemKey?.GetValue("KeyboardLayout");
                    if (value is int signed)
                    {
                        var raw = unchecked((uint)signed);
                        result[raw] = unchecked((nint)(int)raw);
                    }
                    else if (value is long wide)
                    {
                        var raw = unchecked((uint)wide);
                        result[raw] = unchecked((nint)(int)raw);
                    }
                }
            }
        }
        return result;
    }
    private static Registration? ReadRegistration(string id)
    {
        if (!TryKlid(id, out var klid)) return null;
        using var key = Registry.LocalMachine.OpenSubKey($@"{LayoutRoot}\{klid}");
        if (key is null) return null;
        var language = (ushort)(uint.Parse(klid, NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0xFFFF);
        ushort? device = key.GetValue("Layout Id") is string value && ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        return new(klid, language, device, key.GetValue("Layout Text") as string);
    }
    internal static uint ExpectedHandle(Registration registration)
    {
        var klid = uint.Parse(registration.Klid, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if ((klid & 0xF0000000) == 0xE0000000) return klid; // traditional IME layout ID
        var high = registration.DeviceId is ushort device ? (uint)(0xF000 | device) : registration.LanguageId;
        return (high << 16) | registration.LanguageId;
    }
    private static bool Matches(nint handle, Registration registration)
        => unchecked((uint)(long)handle) == ExpectedHandle(registration);

    private static string? KlidForHandle(nint handle, IReadOnlyList<Registration> known)
    {
        var match = known.FirstOrDefault(x => Matches(handle, x));
        if (match is not null) return match.Klid;
        var raw = unchecked((uint)(long)handle); var high = raw >> 16; var language = raw & 0xFFFF;
        if (high == language) return language.ToString("X8");
        if ((raw & 0xF0000000) == 0xE0000000 && ReadRegistration(raw.ToString("X8")) is not null) return raw.ToString("X8");
        // A loaded variant need not be in Preload (for example, one supplied by a remote session).
        using var root = Registry.LocalMachine.OpenSubKey(LayoutRoot);
        if (root is null) return null;
        foreach (var id in root.GetSubKeyNames())
        {
            if (!id.EndsWith(language.ToString("X4"), StringComparison.OrdinalIgnoreCase)) continue;
            var registration = ReadRegistration(id);
            if (registration is not null && Matches(handle, registration)) return registration.Klid;
        }
        return null;
    }
    internal static List<KeyboardLayout> BuildCatalog(IReadOnlyList<nint> loaded, IReadOnlyList<Registration> configured)
    {
        var result = new List<KeyboardLayout>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in configured)
        {
            if (!seen.Add(registration.Klid)) continue;
            var handle = loaded.FirstOrDefault(x => Matches(x, registration));
            if (handle == 0) handle = registration.ProfileHandle;
            result.Add(Describe(registration, handle));
        }
        foreach (var handle in loaded)
        {
            var klid = KlidForHandle(handle, configured) ?? Id(handle);
            if (!seen.Add(klid)) continue;
            var registration = ReadRegistration(klid) ?? new(klid, (ushort)((long)handle & 0xFFFF), null, null);
            result.Add(Describe(registration, handle));
        }
        return result;
    }
    private static KeyboardLayout Describe(Registration registration, nint handle)
    {
        var locale = new StringBuilder(85); Win32.LCIDToLocaleName(registration.LanguageId, locale, locale.Capacity, 0);
        var display = new StringBuilder(256); Win32.GetLocaleInfoEx(locale.ToString(), 2, display, display.Capacity);
        var iso = new StringBuilder(16); Win32.GetLocaleInfoEx(locale.ToString(), 0x59, iso, iso.Capacity);
        var code = iso.Length == 0 ? "??" : iso.ToString().ToUpperInvariant(); if (code == "UK") code = "UA";
        var name = display.Length == 0 ? registration.LayoutText ?? $"Layout {registration.Klid}" : display.ToString();
        if (registration.DeviceId.HasValue && registration.LayoutText is not null) name += $" · {registration.LayoutText}";
        return new(registration.Klid, handle, registration.LanguageId, name, code);
    }
    internal static List<KeyboardLayout> GetInstalled()
    {
        var substitutions = ReadSubstitutions(); var profileHandles = ReadProfileHandles();
        var configured = new List<Registration>();
        foreach (var id in ReadConfiguredIds(substitutions))
        {
            var registration = ReadRegistration(id);
            if (registration is null) continue; // a stale user profile is not an installed keyboard
            if (profileHandles.TryGetValue(ExpectedHandle(registration), out var profileHandle)) registration = registration with { ProfileHandle = profileHandle };
            configured.Add(registration);
        }
        var handles = new nint[Math.Max(Win32.GetKeyboardLayoutList(0, null), 1)];
        var count = Win32.GetKeyboardLayoutList(handles.Length, handles);
        return BuildCatalog(handles.Take(count).ToArray(), configured);
    }
    internal static string? ResolveId(nint handle, IReadOnlyList<KeyboardLayout> catalog)
    {
        var raw = unchecked((uint)(long)handle);
        var layout = catalog.FirstOrDefault(x => x.Handle != 0 && unchecked((uint)(long)x.Handle) == raw);
        if (layout is not null) return layout.Id;
        var known = catalog.Select(x => ReadRegistration(x.Id)).Where(x => x is not null).Select(x => x!).ToList();
        return KlidForHandle(handle, known) ?? Id(handle);
    }
    internal static void MigrateConfiguration(Configuration config, IReadOnlyList<KeyboardLayout> catalog)
    {
        var substitutions = ReadSubstitutions();
        string Canonical(string value)
        {
            var direct = catalog.FirstOrDefault(x => x.Id.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (direct is not null) return direct.Id;
            if (!uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw)) return value;
            var byHandle = catalog.FirstOrDefault(x => x.Handle != 0 && unchecked((uint)(long)x.Handle) == raw);
            if (byHandle is not null) return byHandle.Id;
            var candidate = (raw >> 16) == (raw & 0xFFFF) ? (raw & 0xFFFF).ToString("X8") : raw.ToString("X8");
            var substituted = ResolveSubstitution(candidate, substitutions);
            return catalog.FirstOrDefault(x => x.Id.Equals(substituted, StringComparison.OrdinalIgnoreCase))?.Id ?? value;
        }
        if (config.PrimaryLayouts is not null)
            for (var i = 0; i < config.PrimaryLayouts.Count; i++)
                if (config.PrimaryLayouts[i] is string id) config.PrimaryLayouts[i] = Canonical(id);
        if (config.ExtraLayouts is not null)
            for (var i = 0; i < config.ExtraLayouts.Count; i++)
                if (config.ExtraLayouts[i] is { LayoutId: string id } extra) config.ExtraLayouts[i] = extra with { LayoutId = Canonical(id) };
    }
    internal static bool TryResolveHandle(KeyboardLayout layout, IReadOnlyList<KeyboardLayout> catalog, out nint handle)
    {
        handle = 0;
        var handles = new nint[Math.Max(Win32.GetKeyboardLayoutList(0, null), 1)];
        var count = Win32.GetKeyboardLayoutList(handles.Length, handles);
        foreach (var candidate in handles.Take(count))
            if (ResolveId(candidate, catalog) == layout.Id) { handle = candidate; return true; }
        // TSF has an exact native profile handle even when the current process's list omits it.
        if (layout.Handle != 0) { handle = layout.Handle; return true; }
        return false;
    }
    internal static bool PrepareSelected(Configuration config, out string? error)
    {
        error = null; var catalog = GetInstalled();
        var foreground = Win32.GetForegroundWindow(); Win32.GetWindowThreadProcessId(foreground, out var process);
        var ownsFocus = process == Win32.GetCurrentProcessId();
        var previous = Win32.GetKeyboardLayout(0);
        try
        {
            foreach (var id in config.PrimaryLayouts.Concat(config.ExtraLayouts.Select(x => x.LayoutId)))
            {
                var layout = catalog.FirstOrDefault(x => x.Id == id);
                if (layout is null) { error = "The selected keyboard layout is no longer installed."; return false; }
                if (TryResolveHandle(layout, catalog, out _)) continue;
                if (!ownsFocus) continue; // never steal focus or activate an unrequested layout during background work
                var native = LayoutNative.LoadKeyboardLayoutW(id, 2); // honor configured substitution; never KLF_REORDER/SETPROCESS
                if (native == 0 || ResolveId(native, catalog) != id)
                { error = "Windows could not load the selected keyboard layout. Add it in Windows language settings and try again."; return false; }
            }
            return true;
        }
        finally { if (ownsFocus && Win32.GetKeyboardLayout(0) != previous) LayoutNative.ActivateKeyboardLayout(previous, 0); }
    }
    internal static (nint Window, uint Thread, string? LayoutId) Foreground(IReadOnlyList<KeyboardLayout>? catalog = null)
    {
        var window = Win32.GetForegroundWindow(); if (window == 0) return (0, 0, null);
        var thread = Win32.GetWindowThreadProcessId(window, out _); var handle = Win32.GetKeyboardLayout(thread);
        return (window, thread, catalog is null ? Id(handle) : ResolveId(handle, catalog));
    }
}
