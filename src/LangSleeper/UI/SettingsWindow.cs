using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LangSleeper.Core;
using LangSleeper.Native;
using LangSleeper.Services;

namespace LangSleeper.UI;

internal sealed partial class SettingsWindow : NativeWindow
{
    private const int WindowWidth = 820, WindowHeight = 600, VisibleExtras = 3;
    private const int First = 101, Second = 102, Cycle = 103, Save = 104, WindowsLanguages = 105,
        Startup = 106, Indicator = 107, Animate = 108, Preview = 109, Language = 110,
        Close = 111, Minimize = 112, LanguagesTab = 113, AppearanceTab = 114,
        ThemeSystem = 115, ThemeLight = 116, ThemeDark = 117, ScrollUp = 118, ScrollDown = 119,
        ExtraKeyBase = 1000, ExtraRemoveBase = 2000;
    private readonly App app;
    private Configuration draft;
    private List<KeyboardLayout> layouts;
    private Drawing drawing;
    private Palette palette;
    private bool glass, dirty, hasError, closing, refreshingCatalog;
    private string? status;
    private readonly Dictionary<int, nint> controls = [];
    private readonly Dictionary<int, Win32.RECT> positions = [];
    private readonly Dictionary<int, string> buttonText = [];
    private readonly Dictionary<int, Hotkey> shortcuts = [];
    private readonly Dictionary<int, KeyboardLayout> extraTargets = [];
    private readonly Dictionary<int, double> hoverProgress = [], togglePositions = [];
    private readonly HashSet<int> visibleControls = [];
    private readonly Win32.SubclassProc inputProc;
    private readonly Stopwatch transition = new();
    private int view, hovered, capturing, extraOffset, failedShortcut;
    private double viewProgress = 1;

    private string T(string english, string russian) => UiText.Get(draft.UiLanguage, english, russian);
    internal SettingsWindow(App app, bool show = true)
    {
        this.app = app; draft = app.Config.Copy(); layouts = LayoutCatalog.GetInstalled();
        drawing = new(Win32.GetDpiForSystem() / 96.0); palette = ResolvePalette(); inputProc = Input;
        var monitor = new Win32.MONITORINFO { Size = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetCursorPos(out var pointer); Win32.GetMonitorInfoW(Win32.MonitorFromPoint(pointer, 2), ref monitor);
        drawing.Scale = Math.Min(drawing.Scale, Math.Min((monitor.Work.Height - 32) / (double)WindowHeight, (monitor.Work.Width - 32) / (double)WindowWidth));
        var width = drawing.S(WindowWidth); var height = drawing.S(WindowHeight);
        Create("LangSleeper.Settings", "LangSleeper", Win32.WS_POPUP | Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX,
            0, monitor.Work.Left + (monitor.Work.Width - width) / 2, monitor.Work.Top + (monitor.Work.Height - height) / 2, width, height);
        glass = Backdrop.Apply(Handle, palette.Dark); BuildControls(); RefreshLabels();
        if (show) { Win32.ShowWindow(Handle, 5); Win32.UpdateWindow(Handle); }
        if (app.StartupWarning is not null) SetStatus(LocalizeError(app.StartupWarning), true);
    }
    private Palette ResolvePalette() => new(draft.Theme == "dark" || draft.Theme == "system" && Palette.SystemDark());
    private nint Button(int id, int x, int y, int width, int height)
    {
        // Input/accessibility only. The entire visible scene is painted once by the parent,
        // so child HWNDs cannot punch rectangular zero-alpha holes through the glass.
        var hwnd = Win32.CreateWindowExW(Win32.EX_TRANSPARENT, "BUTTON", "", Win32.WS_CHILD | Win32.WS_VISIBLE | Win32.WS_TABSTOP | 0xB,
            drawing.S(x), drawing.S(y), drawing.S(width), drawing.S(height), Handle, id, Win32.Instance, 0);
        controls[id] = hwnd; positions[id] = new(x, y, width, height); visibleControls.Add(id);
        Win32.SetWindowSubclass(hwnd, inputProc, (nuint)id, 0);
        return hwnd;
    }
    private void BuildControls()
    {
        Button(LanguagesTab, 16, 108, 144, 40); Button(AppearanceTab, 16, 158, 144, 40);
        Button(Language, 20, 544, 136, 32);
        Button(Minimize, 734, 16, 28, 28); Button(Close, 776, 16, 28, 28);
        Button(First, 210, 154, 172, 40); Button(Second, 412, 154, 172, 40); Button(Cycle, 620, 154, 148, 40);
        shortcuts[Cycle] = draft.CycleHotkey;
        Button(WindowsLanguages, 194, 501, 242, 28); Button(ScrollUp, 728, 249, 24, 26); Button(ScrollDown, 760, 249, 24, 26);
        Button(ThemeSystem, 194, 146, 188, 48); Button(ThemeLight, 396, 146, 188, 48); Button(ThemeDark, 598, 146, 188, 48);
        Button(Indicator, 742, 257, 40, 24); Button(Animate, 742, 342, 40, 24); Button(Startup, 742, 427, 40, 24);
        Button(Preview, 194, 490, 190, 32); Button(Save, 650, 550, 136, 34);
        RefreshExtraControls(); ApplyVisibility();
    }
    private List<KeyboardLayout> AdditionalLayouts() => layouts.Where(x => !draft.PrimaryLayouts.Contains(x.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    private void RefreshExtraControls()
    {
        EndCapture();
        foreach (var id in controls.Keys.Where(x => x >= ExtraKeyBase).ToArray())
        {
            Win32.DestroyWindow(controls[id]); controls.Remove(id); positions.Remove(id); buttonText.Remove(id);
            shortcuts.Remove(id); extraTargets.Remove(id); visibleControls.Remove(id); hoverProgress.Remove(id);
        }
        var extras = AdditionalLayouts(); extraOffset = Math.Clamp(extraOffset, 0, Math.Max(0, extras.Count - VisibleExtras));
        for (var row = 0; row < Math.Min(VisibleExtras, extras.Count - extraOffset); row++)
        {
            var layout = extras[extraOffset + row]; var keyId = ExtraKeyBase + row; var removeId = ExtraRemoveBase + row;
            extraTargets[keyId] = layout; extraTargets[removeId] = layout;
            Button(keyId, 548, 312 + row * 61, 196, 36);
            var assignment = draft.ExtraLayouts.FirstOrDefault(x => string.Equals(x.LayoutId, layout.Id, StringComparison.OrdinalIgnoreCase));
            if (assignment is not null) shortcuts[keyId] = assignment.Hotkey;
            Button(removeId, 754, 317 + row * 61, 26, 26);
        }
        ApplyVisibility(); RefreshLabels();
    }
    private void ShowControl(int id, bool visible)
    {
        if (!controls.TryGetValue(id, out var hwnd)) return;
        if (visible) visibleControls.Add(id); else visibleControls.Remove(id);
        Win32.ShowWindow(hwnd, visible ? 5 : 0);
    }
    private void ApplyVisibility()
    {
        foreach (var id in new[] { First, Second, Cycle, WindowsLanguages }) ShowControl(id, view == 0);
        var extras = AdditionalLayouts();
        ShowControl(ScrollUp, view == 0 && extras.Count > VisibleExtras); ShowControl(ScrollDown, view == 0 && extras.Count > VisibleExtras);
        if (controls.TryGetValue(ScrollUp, out var up)) Win32.EnableWindow(up, extraOffset > 0);
        if (controls.TryGetValue(ScrollDown, out var down)) Win32.EnableWindow(down, extraOffset + VisibleExtras < extras.Count);
        foreach (var id in extraTargets.Keys)
            ShowControl(id, view == 0 && (id < ExtraRemoveBase || draft.ExtraLayouts.Any(e => e.LayoutId == extraTargets[id].Id)));
        foreach (var id in new[] { ThemeSystem, ThemeLight, ThemeDark, Indicator, Animate, Startup, Preview }) ShowControl(id, view == 1);
    }
    private string LayoutName(KeyboardLayout? layout, bool compact = false)
    {
        if (layout is null) return T("Choose", "Выбрать");
        var name = layout.Name;
        if (!UiText.IsRussian(draft.UiLanguage))
        {
            var locale = new StringBuilder(85); Win32.LCIDToLocaleName(layout.LanguageId, locale, locale.Capacity, 0);
            var text = new StringBuilder(256); Win32.GetLocaleInfoEx(locale.ToString(), 0x72, text, text.Capacity);
            if (text.Length > 0) name = text.ToString();
        }
        if (!compact) return name;
        var separator = name.IndexOfAny(['(', '·']); return separator > 0 ? name[..separator].Trim() : name;
    }
    private void RefreshLabels()
    {
        SetLabel(LanguagesTab, T("Languages", "Языки")); SetLabel(AppearanceTab, T("Appearance", "Оформление"));
        SetLabel(Language, UiText.IsRussian(draft.UiLanguage) ? "Русский" : "English");
        SetLabel(First, LayoutName(layouts.FirstOrDefault(x => x.Id == draft.PrimaryLayouts.ElementAtOrDefault(0)), true));
        SetLabel(Second, LayoutName(layouts.FirstOrDefault(x => x.Id == draft.PrimaryLayouts.ElementAtOrDefault(1)), true));
        SetLabel(Cycle, draft.CycleHotkey.ToString()); SetLabel(Save, T("Save", "Сохранить"));
        SetLabel(WindowsLanguages, T("Add a layout in Windows ↗", "Добавить раскладку в Windows ↗"));
        SetLabel(Startup, T("Launch with Windows", "Запускать с Windows")); SetLabel(Indicator, T("Language indicator", "Индикатор языка"));
        SetLabel(Animate, T("Smooth transitions", "Плавные переходы")); SetLabel(Preview, T("Preview indicator", "Показать индикатор"));
        SetLabel(ThemeSystem, T("System", "Системная")); SetLabel(ThemeLight, T("Light", "Светлая")); SetLabel(ThemeDark, T("Dark", "Тёмная"));
        SetLabel(Minimize, T("Minimize", "Свернуть")); SetLabel(Close, T("Close", "Закрыть"));
        SetLabel(ScrollUp, T("Previous languages", "Предыдущие языки")); SetLabel(ScrollDown, T("More languages", "Следующие языки"));
        foreach (var (id, layout) in extraTargets)
        {
            if (id >= ExtraRemoveBase) SetLabel(id, T("Remove shortcut", "Убрать сочетание") + " · " + LayoutName(layout));
            else SetLabel(id, shortcuts.TryGetValue(id, out var key) ? key.ToString() : T("Assign shortcut", "Назначить сочетание"));
        }
        Win32.SetWindowTextW(Handle, T("LangSleeper — Settings", "LangSleeper — Настройки")); Repaint();
    }
    private void SetLabel(int id, string text)
    { buttonText[id] = text; if (controls.TryGetValue(id, out var hwnd)) Win32.SetWindowTextW(hwnd, text); }
    private void Repaint() { if (Handle != 0) Win32.InvalidateRect(Handle, 0, false); }
    private void SetStatus(string text, bool error) { status = text.Replace('\r', ' ').Replace('\n', ' '); hasError = error; Repaint(); }
    private string LocalizeError(string error) => UiText.Error(draft.UiLanguage, error);
    private bool ShortcutConflict(int id) => id == failedShortcut || shortcuts.TryGetValue(id, out var key)
        && draft.ExtraLayouts.Select(x => x.Hotkey).Append(draft.CycleHotkey).Count(x => x == key) > 1;
    private void Changed()
    {
        dirty = true; failedShortcut = 0; var errors = ConfigurationValidator.Validate(draft, layouts);
        hasError = errors.Count > 0;
        status = hasError ? LocalizeError(errors[0]) : T("Unsaved changes", "Есть несохранённые изменения");
        Win32.EnableWindow(controls[Save], !hasError); RefreshLabels();
    }
    private int Choose(string[] options, int current, int id)
    {
        MenuTheme.Apply(palette.Dark); var menu = Win32.CreatePopupMenu();
        try
        {
            for (var i = 0; i < options.Length; i++) Win32.AppendMenuW(menu, i == current ? 8u : 0u, (nuint)(i + 1), options[i]);
            Win32.GetWindowRect(controls[id], out var rect);
            return (int)Win32.TrackPopupMenu(menu, 0x100, rect.Left, rect.Bottom + drawing.S(6), 0, Handle, 0) - 1;
        }
        finally { Win32.DestroyMenu(menu); }
    }
    private void ChooseMain(int id)
    {
        RefreshCatalog(); var index = id == First ? 0 : 1;
        var other = draft.PrimaryLayouts.ElementAtOrDefault(1 - index); var current = draft.PrimaryLayouts.ElementAtOrDefault(index);
        var available = layouts.Where(x => x.Id != other).ToList();
        var choice = Choose(available.Select(x => $"{x.Code}    {LayoutName(x)}").ToArray(), available.FindIndex(x => x.Id == current), id);
        if (choice < 0) return;
        while (draft.PrimaryLayouts.Count < 2) draft.PrimaryLayouts.Add("");
        draft.PrimaryLayouts[index] = available[choice].Id;
        draft.ExtraLayouts.RemoveAll(x => x.LayoutId == available[choice].Id);
        extraOffset = 0; RefreshExtraControls(); Changed();
    }
    private void BindExtra(int id)
    {
        if (!extraTargets.TryGetValue(id, out var layout)) return;
        var hotkey = draft.ExtraLayouts.FirstOrDefault(x => x.LayoutId == layout.Id)?.Hotkey;
        if (hotkey is null)
        {
            var preferred = layout.Code == "UA" ? 'U' : layout.Code.Length > 0 && layout.Code[0] is >= 'A' and <= 'Z' ? layout.Code[0] : 'U';
            var candidates = new[] { (uint)preferred }.Concat(Enumerable.Range('A', 26).Select(x => (uint)x)).Concat(Enumerable.Range(0x7C, 12).Select(x => (uint)x));
            hotkey = candidates.Select(key => new Hotkey(7, key)).FirstOrDefault(x => draft.CycleHotkey != x && draft.ExtraLayouts.All(e => e.Hotkey != x));
            if (hotkey is null) { SetStatus(T("No default shortcut available. Remove an unused assignment.", "Уберите ненужное назначение, чтобы освободить сочетание."), true); return; }
            draft.ExtraLayouts.Add(new(layout.Id, hotkey)); shortcuts[id] = hotkey; ApplyVisibility(); Changed();
        }
        StartCapture(id);
    }
    private void StartCapture(int id)
    {
        if (capturing == id || !shortcuts.ContainsKey(id)) return;
        EndCapture(); capturing = id; app.SetCapturing(true); Win32.SetFocus(controls[id]);
        SetLabel(id, T("Press keys…", "Нажмите клавиши…")); Repaint();
    }
    private void EndCapture()
    {
        if (capturing == 0) return;
        var id = capturing; capturing = 0; app.SetCapturing(false);
        if (shortcuts.TryGetValue(id, out var key)) SetLabel(id, key.ToString()); Repaint();
    }
    private bool Toggle(int id) => id == Startup ? draft.AutoStart : id == Indicator ? draft.ShowIndicator : draft.Animate;
    private void AnimateToggle(int id)
    {
        if (draft.Animate && Palette.AnimationsEnabled) { togglePositions[id] = Toggle(id) ? 0 : 1; Win32.SetTimer(Handle, 2, 16, 0); }
        else togglePositions.Remove(id);
    }
    private void ChangeView(int next)
    {
        if (view == next) return;
        EndCapture(); view = next; ApplyVisibility();
        viewProgress = draft.Animate && Palette.AnimationsEnabled ? 0 : 1;
        if (viewProgress < 1) { transition.Restart(); Win32.SetTimer(Handle, 2, 16, 0); }
        Repaint();
    }
    private void ScrollExtras(int delta)
    { var max = Math.Max(0, AdditionalLayouts().Count - VisibleExtras); var next = Math.Clamp(extraOffset + delta, 0, max); if (next != extraOffset) { extraOffset = next; RefreshExtraControls(); } }
    private void RefreshCatalog()
    {
        if (refreshingCatalog || closing) return; refreshingCatalog = true;
        try
        {
            var refreshed = LayoutCatalog.GetInstalled();
            if (refreshed.Select(x => x.Id).SequenceEqual(layouts.Select(x => x.Id))) return;
            layouts = refreshed; RefreshExtraControls(); RefreshLabels();
        }
        finally { refreshingCatalog = false; }
    }
    private void Command(int id)
    {
        if (id >= ExtraRemoveBase && extraTargets.TryGetValue(id, out var remove))
        { EndCapture(); draft.ExtraLayouts.RemoveAll(x => x.LayoutId == remove.Id); RefreshExtraControls(); Changed(); return; }
        if (id >= ExtraKeyBase && extraTargets.ContainsKey(id)) { BindExtra(id); return; }
        switch (id)
        {
            case First: case Second: ChooseMain(id); break;
            case Cycle: StartCapture(id); break;
            case LanguagesTab: ChangeView(0); break; case AppearanceTab: ChangeView(1); break;
            case ScrollUp: ScrollExtras(-1); break; case ScrollDown: ScrollExtras(1); break;
            case ThemeSystem: case ThemeLight: case ThemeDark:
                draft.Theme = id == ThemeSystem ? "system" : id == ThemeLight ? "light" : "dark";
                palette = ResolvePalette(); glass = Backdrop.Apply(Handle, palette.Dark); Changed(); break;
            case Language:
                var language = Choose(["Русский", "English"], UiText.IsRussian(draft.UiLanguage) ? 0 : 1, Language);
                if (language >= 0) { draft.UiLanguage = language == 0 ? "ru" : "en"; Changed(); } break;
            case Startup: draft.AutoStart = !draft.AutoStart; AnimateToggle(id); Changed(); break;
            case Indicator: draft.ShowIndicator = !draft.ShowIndicator; AnimateToggle(id); Changed(); break;
            case Animate: draft.Animate = !draft.Animate; AnimateToggle(id); Changed(); break;
            case Preview:
                var preview = AdditionalLayouts().FirstOrDefault() ?? layouts.FirstOrDefault();
                if (preview is not null) app.Preview(preview, draft); break;
            case Save: case 1:
                EndCapture(); var errors = ConfigurationValidator.Validate(draft, layouts);
                if (errors.Count > 0) { SetStatus(LocalizeError(errors[0]), true); break; }
                if (app.TrySave(draft.Copy(), out var error)) { dirty = false; SetStatus(T("Saved", "Сохранено"), false); }
                else
                {
                    failedShortcut = shortcuts.FirstOrDefault(x => error!.Contains(x.Value.ToString(), StringComparison.Ordinal)).Key;
                    SetStatus(LocalizeError(error!), true);
                }
                break;
            case WindowsLanguages: Win32.ShellExecuteW(Handle, "open", "ms-settings:regionlanguage", null, null, 1); break;
            case Minimize: Win32.ShowWindow(Handle, 6); break;
            case Close: Dispose(); break;
        }
    }
    private nint Input(nint hwnd, uint message, nuint wParam, nint lParam, nuint idValue, nuint data)
    {
        var id = (int)idValue;
        if (message == Win32.WM_ERASEBKGND) return 1;
        if (message == Win32.WM_PAINT) { Win32.BeginPaint(hwnd, out var paint); Win32.EndPaint(hwnd, ref paint); return 0; }
        if (message == 0x20) { Win32.SetCursor(Win32.LoadCursorW(0, 32649)); return 1; }
        if (message == 8 && capturing == id) EndCapture();
        if (message == 0x87 && capturing == id) return 4;
        if (message is 0x100 or 0x104 && capturing == id)
        {
            var key = (uint)wParam;
            if (key is 0x1B or 0x09) { EndCapture(); Win32.SetFocus(controls[Save]); return 0; }
            if (key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C) return 0;
            uint modifiers = 0;
            if (Win32.GetKeyState(0x11) < 0) modifiers |= 2; if (Win32.GetKeyState(0x12) < 0) modifiers |= 1; if (Win32.GetKeyState(0x10) < 0) modifiers |= 4;
            var shortcut = new Hotkey(modifiers, key);
            if (!shortcut.IsValid) { SetStatus(T("Use Ctrl, Alt or Shift with a letter or Space.", "Нажмите Ctrl, Alt или Shift с буквой или пробелом."), true); return 0; }
            shortcuts[id] = shortcut;
            if (id == Cycle) draft.CycleHotkey = shortcut;
            else if (extraTargets.TryGetValue(id, out var layout))
            { draft.ExtraLayouts.RemoveAll(x => x.LayoutId == layout.Id); draft.ExtraLayouts.Add(new(layout.Id, shortcut)); }
            EndCapture(); Win32.SetFocus(controls[Save]); Changed(); return 0;
        }
        if (message is 0x101 or 0x105 or 0x102 or 0x106 && capturing == id) return 0;
        if (message == 0x200 && hovered != id)
        {
            hovered = id; Win32.SetTimer(Handle, 2, 16, 0);
            var tracking = new Win32.TRACKMOUSEEVENT { Size = (uint)Marshal.SizeOf<Win32.TRACKMOUSEEVENT>(), Flags = 2, Hwnd = hwnd }; Win32.TrackMouseEvent(ref tracking); Repaint();
        }
        if (message == 0x2A3 && hovered == id) { hovered = 0; Win32.SetTimer(Handle, 2, 16, 0); Repaint(); }
        var result = Win32.DefSubclassProc(hwnd, message, wParam, lParam);
        if (message is 7 or 8 or 0x201 or 0x202 or 0xF or 0xA) Repaint();
        return result;
    }
    private void Tick()
    {
        var pending = false;
        foreach (var id in visibleControls)
        {
            var current = hoverProgress.GetValueOrDefault(id); var target = hovered == id ? 1.0 : 0;
            var next = draft.Animate && Palette.AnimationsEnabled ? current + (target - current) * 0.3 : target;
            if (Math.Abs(next - target) < 0.006) next = target; else pending = true;
            hoverProgress[id] = next;
        }
        foreach (var id in togglePositions.Keys.ToArray())
        {
            var target = Toggle(id) ? 1.0 : 0; var current = togglePositions[id] + (target - togglePositions[id]) * 0.3;
            if (Math.Abs(target - current) < 0.006) togglePositions.Remove(id); else { togglePositions[id] = current; pending = true; }
        }
        if (viewProgress < 1) { viewProgress = Math.Clamp(transition.Elapsed.TotalMilliseconds / 150, 0, 1); pending |= viewProgress < 1; }
        if (!pending) Win32.KillTimer(Handle, 2); Repaint();
    }
    protected override nint ProcessMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case 0x83: if (wParam != 0) return 0; break;
            case 0x84:
                var point = new Win32.POINT(unchecked((short)((long)lParam & 0xFFFF)), unchecked((short)(((long)lParam >> 16) & 0xFFFF)));
                Win32.ScreenToClient(Handle, ref point); return point.Y < drawing.S(94) ? 2 : 1;
            case Win32.WM_ERASEBKGND: return 1;
            case Win32.WM_PAINT: Paint(); return 0;
            case Win32.WM_DRAWITEM: return 1;
            case Win32.WM_COMMAND: if ((wParam >> 16) == 0) Command((int)(wParam & 0xFFFF)); return 0;
            case Win32.WM_TIMER: if (wParam == 2) Tick(); return 0;
            case 0x20A: if (view == 0) ScrollExtras(unchecked((short)(wParam >> 16)) > 0 ? -1 : 1); return 0;
            case 6: if ((wParam & 0xFFFF) != 0) RefreshCatalog(); break;
            case Win32.WM_SETTINGCHANGE: palette = ResolvePalette(); glass = Backdrop.Apply(Handle, palette.Dark); Repaint(); return 0;
            case Win32.WM_DPICHANGED:
                EndCapture(); var rect = Marshal.PtrToStructure<Win32.RECT>(lParam);
                foreach (var control in controls.Values) Win32.DestroyWindow(control); controls.Clear(); positions.Clear(); visibleControls.Clear();
                drawing.Dispose(); drawing = new((wParam & 0xFFFF) / 96.0);
                Win32.SetWindowPos(Handle, 0, rect.Left, rect.Top, drawing.S(WindowWidth), drawing.S(WindowHeight), 0x14);
                BuildControls(); RefreshLabels(); return 0;
            case Win32.WM_CLOSE: Dispose(); return 0;
        }
        return base.ProcessMessage(message, wParam, lParam);
    }
    protected override void OnDestroyed() { closing = true; EndCapture(); app.SettingsClosed(); drawing.Dispose(); MenuTheme.Apply(Palette.SystemDark()); }
}
