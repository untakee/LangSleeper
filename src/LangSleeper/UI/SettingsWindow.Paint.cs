using LangSleeper.Native;

namespace LangSleeper.UI;

internal sealed partial class SettingsWindow
{
    private uint Tint(byte alpha) => Palette.Argb(palette.Dark ? Palette.Hex(0xFFFFFF) : Palette.Hex(0x202427), alpha);
    private void Paint()
    {
        var dc = Win32.BeginPaint(Handle, out var paint);
        try
        {
            using var surface = new PaintSurface(drawing.S(WindowWidth), drawing.S(WindowHeight));
            DrawScene(surface.Dc); surface.Present(dc);
        }
        finally { Win32.EndPaint(Handle, ref paint); }
    }

    // Uses the same renderer as the window. The opaque underlay represents the neutral
    // backdrop only; Windows supplies live Acrylic when the actual window is displayed.
    internal void ExportPreview(string path, int page = 0)
    {
        var previousView = view; view = page; viewProgress = 1; ApplyVisibility();
        try
        {
            using var surface = new PaintSurface(drawing.S(WindowWidth), drawing.S(WindowHeight));
            drawing.Box(surface.Dc, 0, 0, WindowWidth, WindowHeight, 0, palette.Background);
            DrawScene(surface.Dc); surface.SavePng(path);
        }
        finally { view = previousView; ApplyVisibility(); }
    }

    private void DrawScene(nint dc)
    {
        drawing.Opacity = 1;
        drawing.GlassBox(dc, 0, 0, WindowWidth, WindowHeight, 0,
            Palette.Argb(palette.Background, glass ? palette.Dark ? (byte)165 : (byte)125 : (byte)255));
        drawing.GlassBox(dc, 176, 8, 636, 584, 17,
            palette.Dark ? 0x142F3438u : 0x65FFFFFFu, Tint(palette.Dark ? (byte)14 : (byte)18),
            palette.Dark ? 0x06181B1Eu : 0x24FFFFFFu);

        drawing.Moon(dc, 25, 35, 22, palette.Text, palette.Background);
        drawing.Text(dc, "LangSleeper", 56, 31, 112, 28, palette.Text, 15, 600);
        drawing.Text(dc, T("KEYBOARD, SIMPLIFIED", "РАСКЛАДКИ БЕЗ ЛИШНЕГО"), 24, 69, 140, 18, palette.Muted, 8, 600);
        foreach (var id in new[] { LanguagesTab, AppearanceTab, Language, Close, Minimize }) DrawControl(dc, id);
        drawing.Text(dc, "0.2", 24, 511, 60, 18, palette.Muted, 10);

        var eased = 1 - Math.Pow(1 - viewProgress, 3);
        drawing.Opacity = eased;
        drawing.Offset(dc, 7 * (1 - eased), 0, () =>
        {
            drawing.Text(dc, view == 0 ? T("Your languages", "Ваши языки") : T("Make it yours", "В вашем стиле"),
                194, 38, 470, 36, palette.Text, 27, 600);
            drawing.Text(dc, view == 0 ? T("Two for every day. The rest, one shortcut away.", "Два на каждый день. Остальные — по своему сочетанию.")
                : T("Quiet details that feel right.", "Тема, индикатор и несколько полезных мелочей."), 195, 80, 590, 22, palette.Muted, 12);
            if (view == 0) DrawLanguages(dc); else DrawAppearance(dc);
            foreach (var id in visibleControls.Where(id => id is not (LanguagesTab or AppearanceTab or Language or Close or Minimize or Save))) DrawControl(dc, id);
        });
        drawing.Opacity = 1;
        drawing.GlassBox(dc, 194, 537, 592, 1, 0, Tint(18));
        var footer = status ?? T("Ready when you are", "Всё готово к работе");
        drawing.Circle(dc, 196, 564, 5, hasError ? palette.Error : dirty ? palette.Muted : palette.Green);
        drawing.Text(dc, footer, 209, 550, 423, 34, hasError ? palette.Error : palette.Muted, 11);
        DrawControl(dc, Save);
    }

    private void DrawLanguages(nint dc)
    {
        drawing.GlassBox(dc, 194, 117, 592, 95, 13, Tint(palette.Dark ? (byte)7 : (byte)4), Tint(16));
        drawing.Text(dc, T("Everyday pair", "Основная пара"), 210, 126, 280, 19, palette.Muted, 11, 600);
        drawing.Text(dc, T("Switch between them", "Переключать пару"), 620, 126, 155, 19, palette.Muted, 10);
        drawing.Text(dc, "⇄", 383, 156, 28, 36, palette.Muted, 19, 400, Win32.DT_CENTER);
        var extras = AdditionalLayouts();
        drawing.Text(dc, T("Extra languages", "Дополнительные языки"), 194, 246, 475, 28, palette.Text, 17, 600);
        drawing.Text(dc, T("Each shortcut opens exactly the language beside it.", "Сочетание сразу включает язык, указанный рядом."), 194, 276, 590, 20, palette.Muted, 11);
        if (extras.Count == 0)
        {
            drawing.Text(dc, T("Add a third language in Windows to assign its shortcut.", "Добавьте третий язык в Windows, чтобы назначить сочетание."), 210, 327, 550, 50, palette.Muted, 13);
            return;
        }
        for (var row = 0; row < Math.Min(VisibleExtras, extras.Count - extraOffset); row++)
        {
            var layout = extras[extraOffset + row]; var y = 304 + row * 61;
            var assigned = draft.ExtraLayouts.Any(x => x.LayoutId == layout.Id);
            drawing.GlassBox(dc, 194, y, 592, 54, 11, Tint(palette.Dark ? (byte)6 : (byte)3));
            drawing.GlassBox(dc, 208, y + 10, 36, 34, 9, Tint(13), Tint(15));
            drawing.Text(dc, layout.Code, 208, y + 10, 36, 34, palette.Text, 12, 600, Win32.DT_CENTER);
            drawing.Text(dc, LayoutName(layout, true), 258, y + 5, 278, 25, palette.Text, 14, 600);
            drawing.Text(dc, assigned ? T("Direct access", "Прямое включение") : T("Choose your shortcut", "Назначьте своё сочетание"), 258, y + 29, 278, 18, palette.Muted, 10);
        }
    }

    private void DrawAppearance(nint dc)
    {
        drawing.Text(dc, T("Theme", "Тема"), 194, 116, 450, 22, palette.Muted, 11, 600);
        var rows = new[]
        {
            (Indicator, 247, T("Language indicator", "Индикатор языка"), T("A small language code. Gone in a second.", "Только код языка. Исчезает через секунду.")),
            (Animate, 332, T("Smooth transitions", "Плавные переходы"), T("Soft fades and subtle movement.", "Мягкое появление и лёгкое движение.")),
            (Startup, 417, T("Launch with Windows", "Запускать с Windows"), T("Starts quietly in the system tray.", "Запускается незаметно, сразу в трей."))
        };
        foreach (var (_, y, title, description) in rows)
        {
            drawing.Text(dc, title, 194, y, 510, 27, palette.Text, 15, 600);
            drawing.Text(dc, description, 194, y + 29, 510, 21, palette.Muted, 12);
            if (y < 417) drawing.GlassBox(dc, 194, y + 64, 592, 1, 0, Tint(12));
        }
    }

    private void DrawControl(nint dc, int id)
    {
        if (!visibleControls.Contains(id) || !positions.TryGetValue(id, out var rect)) return;
        drawing.Offset(dc, rect.Left, rect.Top, () => DrawControlVisual(dc, id, rect.Width, rect.Height));
    }

    private void DrawControlVisual(nint dc, int id, int width, int height)
    {
        var enabled = Win32.IsWindowEnabled(controls[id]);
        var hoveredAmount = hoverProgress.GetValueOrDefault(id);
        var focused = Win32.GetFocus() == controls[id];
        var pressed = (Win32.SendMessageW(controls[id], 0xF2, 0, 0) & 4) != 0;
        var text = buttonText.GetValueOrDefault(id, "");
        var foreground = enabled ? palette.Text : palette.Muted;
        var edge = focused || capturing == id ? Tint(110) : Tint(20);
        var fill = Tint((byte)(8 + hoveredAmount * 15 + (pressed ? 10 : 0)));
        if (ShortcutConflict(id)) { foreground = palette.Error; edge = Palette.Argb(palette.Error, 160); }

        if (id is Close or Minimize or ScrollUp or ScrollDown || id >= ExtraRemoveBase)
        {
            if (hoveredAmount > 0 || focused) drawing.GlassBox(dc, 0, 0, width, height, 7, fill, focused ? edge : null);
            if (id == Minimize) drawing.Line(dc, 9, 14, 19, 14, palette.Muted);
            else if (id is ScrollUp or ScrollDown)
            {
                var sign = id == ScrollUp ? -1 : 1;
                drawing.Line(dc, 8, 13 - 2 * sign, 12, 13 + 2 * sign, palette.Muted);
                drawing.Line(dc, 12, 13 + 2 * sign, 16, 13 - 2 * sign, palette.Muted);
            }
            else { drawing.Line(dc, 10, 10, 17, 17, palette.Muted); drawing.Line(dc, 17, 10, 10, 17, palette.Muted); }
            return;
        }
        if (id is LanguagesTab or AppearanceTab)
        {
            var selected = (id == LanguagesTab) == (view == 0);
            drawing.GlassBox(dc, 0, 0, width, height, 10, selected ? Tint(palette.Dark ? (byte)22 : (byte)14) : Tint((byte)(hoveredAmount * 12)), focused ? edge : null);
            drawing.Text(dc, id == LanguagesTab ? "Aa" : "◐", 12, 0, 24, height, selected ? palette.Text : palette.Muted, 15, 600, Win32.DT_CENTER);
            drawing.Text(dc, text, 44, 0, width - 48, height, selected ? palette.Text : palette.Muted, 12, selected ? 600 : 400);
            return;
        }
        if (id is Startup or Indicator or Animate)
        {
            var amount = togglePositions.GetValueOrDefault(id, Toggle(id) ? 1 : 0);
            var track = Drawing.Blend(palette.Raised, palette.Accent, amount);
            drawing.Box(dc, 0, 0, width, height, height / 2, track, focused ? palette.Muted : null);
            drawing.Circle(dc, 3 + (int)Math.Round((width - height) * amount), 3, height - 6,
                Drawing.Blend(palette.Text, palette.PrimaryText, amount)); return;
        }
        if (id == Save)
        {
            drawing.Box(dc, 0, 0, width, height, 9, enabled ? palette.Accent : palette.Raised, focused ? palette.Muted : null);
            drawing.Text(dc, text, 0, 0, width, height, enabled ? palette.PrimaryText : palette.Muted, 12, 600, Win32.DT_CENTER); return;
        }
        if (id is WindowsLanguages or Language)
        {
            if (hoveredAmount > 0 || focused) drawing.GlassBox(dc, 0, 0, width, height, 7, Tint((byte)(hoveredAmount * 12)), focused ? edge : null);
            drawing.Text(dc, text, 0, 0, width - (id == Language ? 20 : 0), height, palette.Muted, 11);
            if (id == Language) Chevron(dc, width - 13, height / 2, palette.Muted);
            return;
        }
        if (id is ThemeSystem or ThemeLight or ThemeDark)
        {
            var selected = draft.Theme == (id == ThemeSystem ? "system" : id == ThemeLight ? "light" : "dark");
            drawing.GlassBox(dc, 0, 0, width, height, 11, selected ? Tint(20) : fill, selected ? Tint(85) : edge);
            drawing.Text(dc, text, 16, 0, width - 48, height, foreground, 13, selected ? 600 : 400);
            drawing.GlassBox(dc, width - 29, 18, 12, 12, 6, 0, Tint(65));
            if (selected) drawing.Circle(dc, width - 26, 21, 6, palette.Text);
            return;
        }
        drawing.GlassBox(dc, 0, 0, width, height, 8, fill, edge);
        if (id is First or Second)
        {
            var index = id == First ? 0 : 1;
            var code = layouts.FirstOrDefault(x => x.Id == draft.PrimaryLayouts.ElementAtOrDefault(index))?.Code ?? "—";
            drawing.Text(dc, code, 10, 0, 25, height, foreground, 11, 600);
            drawing.Text(dc, text, 44, 0, width - 65, height, foreground, 12);
            Chevron(dc, width - 13, height / 2, palette.Muted);
        }
        else drawing.Text(dc, text, 6, 0, width - 12, height, foreground, id == Preview ? 11 : 12, 400, Win32.DT_CENTER);
    }

    private void Chevron(nint dc, int x, int y, uint color)
    { drawing.Line(dc, x - 3, y - 1, x, y + 2, color); drawing.Line(dc, x, y + 2, x + 3, y - 1, color); }
}
