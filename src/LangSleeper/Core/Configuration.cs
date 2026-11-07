using System.Text.Json;
using System.Text.Json.Serialization;

namespace LangSleeper.Core;

public sealed record Hotkey(uint Modifiers, uint Key)
{
    public static Hotkey DefaultCycle => new(2, 0x20);

    [JsonIgnore]
    public bool IsValid => Modifiers is > 0 and <= 7 &&
        (Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A or >= 0x7C and <= 0x87
         or 0x20 or 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E);

    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        parts.Add(Key switch
        {
            0x20 => "Space", 0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
            0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2D => "Insert", 0x2E => "Delete",
            >= 0x70 and <= 0x87 => $"F{Key - 0x6F}", _ => ((char)Key).ToString()
        });
        return string.Join(" + ", parts);
    }
}

public sealed record ExtraLanguage(string LayoutId, Hotkey Hotkey);

public sealed class Configuration
{
    public int Version { get; set; } = 1;
    public List<string> PrimaryLayouts { get; set; } = [];
    public Hotkey CycleHotkey { get; set; } = Hotkey.DefaultCycle;
    public List<ExtraLanguage> ExtraLayouts { get; set; } = [];
    public bool AutoStart { get; set; } = true;
    public bool ShowIndicator { get; set; } = true;
    public bool Animate { get; set; } = true;
    public string Theme { get; set; } = "system";
    public string UiLanguage { get; set; } = "system";

    public Configuration Copy() => new()
    {
        Version = Version, PrimaryLayouts = [.. PrimaryLayouts], CycleHotkey = CycleHotkey,
        ExtraLayouts = [.. ExtraLayouts], AutoStart = AutoStart, ShowIndicator = ShowIndicator,
        Animate = Animate, Theme = Theme, UiLanguage = UiLanguage
    };

    public static Configuration CreateDefault(IReadOnlyList<KeyboardLayout> layouts)
    {
        var config = new Configuration();
        // Prefer English + Russian when available, otherwise use installed layouts in Windows order.
        var first = layouts.FirstOrDefault(x => x.LanguageId == 0x0409) ?? layouts.FirstOrDefault();
        var second = layouts.FirstOrDefault(x => x.LanguageId == 0x0419 && x.Id != first?.Id)
                     ?? layouts.FirstOrDefault(x => x.Id != first?.Id);
        if (first is not null) config.PrimaryLayouts.Add(first.Id);
        if (second is not null) config.PrimaryLayouts.Add(second.Id);
        uint key = 'U';
        foreach (var layout in layouts.Where(x => !config.PrimaryLayouts.Contains(x.Id)))
        {
            while (config.ExtraLayouts.Any(x => x.Hotkey.Key == key)) key = key == 'Z' ? 'A' : key + 1;
            config.ExtraLayouts.Add(new(layout.Id, new(7, key)));
            key = key == 'Z' ? 'A' : key + 1;
            if (config.ExtraLayouts.Count == 26) break;
        }
        return config;
    }
}

public static class ConfigurationValidator
{
    public static List<string> Validate(Configuration config, IReadOnlyList<KeyboardLayout> installed)
    {
        List<string> errors = [];
        if (config.Version != 1) errors.Add("This settings file was created by a different version of LangSleeper.");
        if (config.PrimaryLayouts is null || config.PrimaryLayouts.Count != 2)
            errors.Add("Choose exactly two main keyboard layouts.");
        if (config.CycleHotkey is null || !config.CycleHotkey.IsValid)
            errors.Add("Choose a shortcut with Ctrl, Alt or Shift and a letter, number, Space or function key (except F12).");
        if (config.Theme is not ("system" or "light" or "dark")) errors.Add("Unknown theme.");
        if (config.UiLanguage is not ("system" or "ru" or "en")) errors.Add("Unknown interface language.");
        if (config.ExtraLayouts is null) errors.Add("Extra layouts cannot be null.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in config.PrimaryLayouts ?? [])
        {
            if (string.IsNullOrWhiteSpace(id) || !installed.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                errors.Add("A selected main layout is not installed in Windows.");
            if (id is not null && !ids.Add(id)) errors.Add("Main layouts must be different.");
        }
        var keys = new HashSet<Hotkey>();
        if (config.CycleHotkey is not null) keys.Add(config.CycleHotkey);
        foreach (var extra in config.ExtraLayouts ?? [])
        {
            if (extra is null) { errors.Add("Invalid extra layout entry."); continue; }
            if (string.IsNullOrWhiteSpace(extra.LayoutId) || !installed.Any(x => x.Id.Equals(extra.LayoutId, StringComparison.OrdinalIgnoreCase)))
                errors.Add("A selected extra layout is not installed in Windows.");
            if (extra.LayoutId is not null && !ids.Add(extra.LayoutId)) errors.Add("A layout can appear only once.");
            if (extra.Hotkey is null || !extra.Hotkey.IsValid) errors.Add("An extra layout has an invalid shortcut.");
            else if (!keys.Add(extra.Hotkey)) errors.Add($"{extra.Hotkey} is assigned more than once.");
        }
        return errors.Distinct().ToList();
    }
}

public sealed class ConfigurationStore(string directory)
{
    public string FilePath => Path.Combine(directory, "settings.json");

    public Configuration? Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(FilePath)) return null;
        try
        {
            var info = new FileInfo(FilePath);
            if (info.Length > 1024 * 1024) throw new InvalidDataException("Settings file exceeds 1 MB.");
            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), ConfigJsonContext.Default.Configuration)
                   ?? throw new InvalidDataException("Settings file is empty.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            warning = $"Could not read settings: {ex.Message}\nThe original file has been kept. Save settings to replace it.";
            return null;
        }
    }

    public void Save(Configuration configuration)
    {
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(configuration, ConfigJsonContext.Default.Configuration);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Configuration))]
internal partial class ConfigJsonContext : JsonSerializerContext;
