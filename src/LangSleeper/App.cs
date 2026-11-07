using System.Diagnostics;
using System.Runtime.InteropServices;
using LangSleeper.Core;
using LangSleeper.Native;
using LangSleeper.Services;
using LangSleeper.UI;

namespace LangSleeper;

internal sealed class App : NativeWindow
{
    internal const uint OpenSettingsMessage = Win32.WM_APP + 2;
    private const uint ForegroundMessage = Win32.WM_APP + 3;
    private readonly ConfigurationStore store;
    private readonly bool allowAutoStart;
    private readonly LayoutPolicy policy = new();
    private List<KeyboardLayout> layouts;
    private readonly HotkeyManager hotkeys;
    private readonly TrayIcon tray;
    private readonly IndicatorWindow indicator = new();
    private readonly Win32.WinEventProc foregroundProc;
    private readonly nint foregroundHook;
    private readonly uint shellMessage, taskbarCreated;
    private SettingsWindow? settings;
    private bool capturing, stopping;
    private string? observedId;
    private PendingSwitch? pending;
    private readonly Stopwatch startup;
    private readonly string? diagnosticsPath;
    private readonly long readyMilliseconds;
    private sealed record PendingSwitch(KeyboardLayout Layout, nint Window, uint Thread, long Start, int Attempts = 0);
    internal Configuration Config { get; private set; }
    internal bool Paused { get; private set; }
    internal string? StartupWarning { get; private set; }
    internal nint SettingsHandle => settings?.Handle ?? 0;
    private string T(string english, string russian) => UiText.Get(Config.UiLanguage, english, russian);

    internal App(string directory, string identity, bool allowAutoStart, string? diagnosticsPath, Stopwatch startup)
    {
        this.allowAutoStart = allowAutoStart; this.diagnosticsPath = diagnosticsPath; this.startup = startup;
        store = new(directory); layouts = LayoutCatalog.GetInstalled();
        var loaded = store.Load(out var warning);
        Config = loaded ?? Configuration.CreateDefault(layouts);
        LayoutCatalog.MigrateConfiguration(Config, layouts);
        var errors = ConfigurationValidator.Validate(Config, layouts);
        if (loaded is not null && errors.Count != 0)
        {
            Config = Configuration.CreateDefault(layouts);
            warning = T("Some saved settings are no longer valid. Review your languages and save again. ", "Сохранённые настройки больше не подходят. Выберите языки заново и сохраните. ") + errors[0];
        }
        StartupWarning = warning is null ? null : UiText.Error(Config.UiLanguage, warning);
        Create("LangSleeper.Host", identity, 0, Win32.EX_TOOLWINDOW, 0, 0, 0, 0);
        hotkeys = new(Handle); tray = new(Handle);
        shellMessage = Win32.RegisterWindowMessageW("SHELLHOOK");
        taskbarCreated = Win32.RegisterWindowMessageW("TaskbarCreated");
        Win32.RegisterShellHookWindow(Handle);
        foregroundProc = (_, _, _, _, _, _, _) => Win32.PostMessageW(Handle, ForegroundMessage, 0, 0);
        foregroundHook = Win32.SetWinEventHook(3, 3, 0, foregroundProc, 0, 0, 0x2); // out-of-context, skip own process
        if (ConfigurationValidator.Validate(Config, layouts).Count == 0)
        {
            if (!hotkeys.TryApply(Config, out var error)) { Paused = true; StartupWarning = UiText.Error(Config.UiLanguage, error!); }
        }
        else { Paused = true; StartupWarning = T("Install at least two keyboard layouts in Windows, then choose them in settings.", "Добавьте хотя бы две раскладки в Windows, затем выберите их в настройках."); }
        RefreshForeground(false);
        try
        {
            // Only normal launches maintain the Run entry. Tests and previews opt out explicitly.
            if (allowAutoStart) AutoStart.Set(Config.AutoStart);
            if (loaded is null && warning is null) store.Save(Config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { StartupWarning = T("Settings or startup registration could not be saved: ", "Не удалось сохранить настройки или автозапуск: ") + ex.Message; }
        if (StartupWarning is not null) tray.Notify(T("LangSleeper needs attention", "Проверьте настройки LangSleeper"), StartupWarning, true);
        readyMilliseconds = startup.ElapsedMilliseconds;
        if (diagnosticsPath is not null) Win32.SetTimer(Handle, 3, 250, 0);
    }

    internal void OpenSettings()
    {
        if (settings is null) settings = new(this);
        Win32.ShowWindow(settings.Handle, 9); Win32.SetForegroundWindow(settings.Handle);
    }
    internal void SettingsClosed() { settings = null; }
    internal void SetCapturing(bool value)
    {
        if (capturing == value || stopping) return;
        capturing = value;
        if (value) hotkeys.Dispose();
        else if (!Paused && !hotkeys.TryApply(Config, out var error))
        { Paused = true; StartupWarning = UiText.Error(Config.UiLanguage, error!); tray.Notify(T("Shortcut unavailable", "Сочетание недоступно"), StartupWarning, true); }
        RefreshForeground(false);
    }
    internal void Preview(KeyboardLayout layout, Configuration draft) => indicator.Show(layout, true, draft);

    internal bool TrySave(Configuration candidate, out string? error)
    {
        layouts = LayoutCatalog.GetInstalled();
        LayoutCatalog.MigrateConfiguration(candidate, layouts);
        var errors = ConfigurationValidator.Validate(candidate, layouts);
        if (errors.Count != 0) { error = errors[0]; return false; }
        if (!LayoutCatalog.PrepareSelected(candidate, out error)) return false;
        layouts = LayoutCatalog.GetInstalled();
        // Even while paused, probe all registrations to surface external conflicts before writing anything.
        if (!hotkeys.TryApply(candidate, out error)) return false;
        string? previousRun = null;
        try
        {
            if (allowAutoStart) { previousRun = AutoStart.Read(); AutoStart.Set(candidate.AutoStart); }
            store.Save(candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            hotkeys.Dispose();
            if (!Paused) hotkeys.TryApply(Config, out _);
            try { if (allowAutoStart) AutoStart.Set(previousRun is not null, previousRun); }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { tray.Notify(T("Startup setting", "Настройка автозапуска"), T("Windows could not restore the previous startup entry. Check the tray's startup option.", "Не удалось восстановить настройку автозапуска. Проверьте её в меню трея."), true); }
            error = T("Could not save settings: ", "Не удалось сохранить настройки: ") + ex.Message;
            return false;
        }
        Config = candidate; StartupWarning = null;
        if (Paused || capturing) hotkeys.Dispose();
        RefreshForeground(false); error = null; return true;
    }

    private void TogglePaused()
    {
        if (Paused)
        {
            var errors = ConfigurationValidator.Validate(Config, layouts);
            if (errors.Count > 0) { tray.Notify(T("Check your languages", "Проверьте языки"), UiText.Error(Config.UiLanguage, errors[0]), true); OpenSettings(); return; }
            if (!hotkeys.TryApply(Config, out var error)) { tray.Notify(T("Shortcut unavailable", "Сочетание недоступно"), UiText.Error(Config.UiLanguage, error!), true); return; }
            Paused = false;
        }
        else { hotkeys.Dispose(); Paused = true; CancelPending(); }
        RefreshForeground(false); if (settings is not null) Win32.InvalidateRect(settings.Handle, 0, false);
    }

    private void OnHotkey(int id)
    {
        if (Paused || capturing || !hotkeys.TryGetAction(id, out var targetId)) return;
        var foreground = LayoutCatalog.Foreground(layouts);
        if (foreground.Window == 0 || foreground.Thread == 0) return;
        var effective = pending is not null && pending.Thread == foreground.Thread ? pending.Layout.Id : foreground.LayoutId;
        policy.Observe(foreground.LayoutId, Config);
        targetId = policy.Resolve(targetId, effective, Config);
        var target = layouts.FirstOrDefault(x => x.Id == targetId);
        if (target is null)
        {
            layouts = LayoutCatalog.GetInstalled(); target = layouts.FirstOrDefault(x => x.Id == targetId);
            if (target is null) { tray.Notify(T("Layout unavailable", "Раскладка недоступна"), T("The selected keyboard layout is no longer installed. Open settings to choose another.", "Выбранная раскладка больше не установлена. Выберите другую в настройках."), true); return; }
        }
        if (!LayoutCatalog.TryResolveHandle(target, layouts, out var nativeHandle))
        {
            tray.Notify(T("Layout not ready", "Раскладка ещё не готова"), T("Open settings and save the selected layout once so Windows can prepare it.", "Откройте настройки и сохраните выбранную раскладку, чтобы Windows подготовила её."), true);
            return;
        }
        target = target with { Handle = nativeHandle };
        var info = new Win32.GUITHREADINFO { Size = (uint)Marshal.SizeOf<Win32.GUITHREADINFO>() };
        var window = Win32.GetGUIThreadInfo(foreground.Thread, ref info) && info.Focus != 0 ? info.Focus : foreground.Window;
        // Posting is nonblocking, including when the target app has stopped pumping messages.
        if (!Win32.PostMessageW(window, 0x50, 0, target.Handle))
        {
            tray.Notify(T("Could not change language", "Не удалось переключить язык"), T("Windows blocked the request. An elevated application requires LangSleeper to run at the same permission level.", "Windows отклонила запрос. Для программы с правами администратора LangSleeper нужны такие же права."), true);
            return;
        }
        pending = new(target, foreground.Window, foreground.Thread, Stopwatch.GetTimestamp());
        Win32.SetTimer(Handle, 1, 16, 0); // bounded acknowledgement, never an idle polling timer
    }

    private void ConfirmPending()
    {
        if (pending is not { } request) return;
        var foreground = LayoutCatalog.Foreground(layouts);
        if (foreground.Window != request.Window || foreground.Thread != request.Thread) { CancelPending(); return; }
        if (foreground.LayoutId == request.Layout.Id)
        {
            CancelPending(); policy.Observe(request.Layout.Id, Config);
            RefreshForeground(false);
            if (Config.ShowIndicator) indicator.Show(request.Layout, Config.PrimaryLayouts.Contains(request.Layout.Id), Config);
            return;
        }
        if (request.Attempts >= 14)
        {
            CancelPending(); RefreshForeground(false);
            tray.Notify(T("Language unchanged", "Язык не изменился"), T("The active application did not accept the requested layout. Some games and input methods manage their own keyboard language.", "Активная программа не приняла запрос. Некоторые игры и методы ввода управляют языком самостоятельно."));
        }
        else pending = request with { Attempts = request.Attempts + 1 };
    }
    private void CancelPending() { pending = null; Win32.KillTimer(Handle, 1); }

    private void RefreshForeground(bool show)
    {
        var foreground = LayoutCatalog.Foreground(layouts); var id = foreground.LayoutId;
        if (id is null) return;
        var layout = layouts.FirstOrDefault(x => x.Id == id);
        if (layout is null) { layouts = LayoutCatalog.GetInstalled(); layout = layouts.FirstOrDefault(x => x.Id == id); }
        var changed = observedId != id; observedId = id; policy.Observe(id, Config);
        tray.Update(layout?.Code ?? "??", Paused, Palette.SystemDark(), language: Config.UiLanguage);
        if (show && changed && !Paused && Config.ShowIndicator && layout is not null && pending is null)
            indicator.Show(layout, Config.PrimaryLayouts.Contains(layout.Id), Config);
    }

    private void ShowMenu()
    {
        MenuTheme.Apply(Palette.SystemDark());
        var menu = Win32.CreatePopupMenu();
        try
        {
            Win32.AppendMenuW(menu, 0, 1, T("Settings…", "Настройки…"));
            Win32.AppendMenuW(menu, Paused ? 8u : 0u, 2, T("Pause shortcuts", "Пауза сочетаний"));
            Win32.AppendMenuW(menu, Config.AutoStart ? 8u : 0u, 3, T("Start with Windows", "Запускать с Windows"));
            Win32.AppendMenuW(menu, 0x800, 0, null); Win32.AppendMenuW(menu, 0, 4, T("Exit LangSleeper", "Выйти из LangSleeper"));
            Win32.GetCursorPos(out var point); Win32.SetForegroundWindow(Handle);
            var command = Win32.TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, Handle, 0);
            Win32.PostMessageW(Handle, 0, 0, 0);
            switch (command)
            {
                case 1: OpenSettings(); break;
                case 2: TogglePaused(); break;
                case 3:
                    var candidate = Config.Copy(); candidate.AutoStart = !candidate.AutoStart;
                    if (!TrySave(candidate, out var error)) tray.Notify(T("Could not save", "Не удалось сохранить"), UiText.Error(Config.UiLanguage, error!), true);
                    break;
                case 4: Dispose(); break;
            }
        }
        finally { Win32.DestroyMenu(menu); }
    }

    private void WriteDiagnostics()
    {
        using var process = Process.GetCurrentProcess();
        var path = Path.GetFullPath(diagnosticsPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // A deliberately small local report, only produced for this explicit command-line option.
        var report = $"{{\n  \"readyWithinMilliseconds\": {readyMilliseconds},\n  \"workingSetBytes\": {process.WorkingSet64},\n  \"privateBytes\": {process.PrivateMemorySize64},\n  \"gdiHandles\": {Win32.GetGuiResources(process.Handle, 0)},\n  \"userHandles\": {Win32.GetGuiResources(process.Handle, 1)},\n  \"dynamicCodeSupported\": {System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported.ToString().ToLowerInvariant()},\n  \"installedLayouts\": {layouts.Count},\n  \"paused\": {Paused.ToString().ToLowerInvariant()}\n}}";
        File.WriteAllText(path, report);
    }
    protected override nint ProcessMessage(uint message, nuint wParam, nint lParam)
    {
        if (message == taskbarCreated) { tray.Recreate(); return 0; }
        if (message == shellMessage)
        {
            if ((uint)wParam == 8) { RefreshForeground(true); ConfirmPending(); }
            return 0;
        }
        switch (message)
        {
            case Win32.WM_HOTKEY: OnHotkey((int)wParam); return 0;
            case OpenSettingsMessage: OpenSettings(); return 0;
            case ForegroundMessage: RefreshForeground(false); return 0;
            case TrayIcon.CallbackMessage:
                switch ((uint)((long)lParam & 0xFFFF))
                { case 0x203: OpenSettings(); break; case 0x205: case 0x7B: ShowMenu(); break; case 0x405: OpenSettings(); break; }
                return 0;
            case Win32.WM_TIMER:
                if (wParam == 1) ConfirmPending();
                if (wParam == 3) { Win32.KillTimer(Handle, 3); WriteDiagnostics(); }
                return 0;
            case Win32.WM_SETTINGCHANGE: RefreshForeground(false); return 0;
            case Win32.WM_CLOSE: Dispose(); return 0;
            case Win32.WM_DESTROY: Win32.PostQuitMessage(0); return 0;
        }
        return base.ProcessMessage(message, wParam, lParam);
    }
    public override void Dispose()
    {
        if (stopping) return; stopping = true;
        settings?.Dispose(); settings = null;
        CancelPending(); hotkeys.Dispose(); indicator.Dispose(); tray.Dispose();
        if (foregroundHook != 0) Win32.UnhookWinEvent(foregroundHook);
        Win32.DeregisterShellHookWindow(Handle); base.Dispose();
    }
}
