namespace LangSleeper.Core;

public sealed record KeyboardLayout(string Id, nint Handle, ushort LanguageId, string Name, string Code);

/// <summary>All primary-cycle decisions live here and never enumerate extra layouts.</summary>
public sealed class LayoutPolicy
{
    private string? lastPrimary;

    public void Observe(string? current, Configuration config)
    {
        if (current is not null && config.PrimaryLayouts.Any(x => x.Equals(current, StringComparison.OrdinalIgnoreCase))) lastPrimary = current;
    }

    /// <summary>A non-null action is a direct extra-language destination, never a cycle request.</summary>
    public string Resolve(string? directLayout, string? current, Configuration config)
    {
        if (directLayout is null) return NextPrimary(current, config);
        var extra = config.ExtraLayouts.FirstOrDefault(x => x.LayoutId.Equals(directLayout, StringComparison.OrdinalIgnoreCase));
        if (extra is null) throw new InvalidOperationException("The direct shortcut must refer to an extra language.");
        return extra.LayoutId;
    }

    public string NextPrimary(string? current, Configuration config)
    {
        if (config.PrimaryLayouts.Count != 2) throw new InvalidOperationException("Two primary layouts are required.");
        var index = config.PrimaryLayouts.FindIndex(x => x.Equals(current, StringComparison.OrdinalIgnoreCase));
        var target = index >= 0 ? config.PrimaryLayouts[1 - index]
            : lastPrimary is not null && config.PrimaryLayouts.Any(x => x.Equals(lastPrimary, StringComparison.OrdinalIgnoreCase)) ? lastPrimary : config.PrimaryLayouts[0];
        return target;
    }
}
