using System.Globalization;

namespace LangSleeper.UI;

internal static class UiText
{
    internal static bool IsRussian(string language) => language == "ru" || language == "system" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
    internal static string Get(string language, string english, string russian) => IsRussian(language) ? russian : english;
    internal static string Error(string language, string error)
    {
        if (!IsRussian(language)) return error;
        if (error.Contains("assigned more than once")) return "Это сочетание уже назначено другому языку.";
        if (error.Contains("is unavailable")) return error.Split(" is unavailable")[0] + " — сочетание занято. Выберите другое.";
        if (error.Contains("different")) return "Выберите два разных основных языка.";
        if (error.Contains("only once")) return "Одна раскладка не может быть одновременно основной и дополнительной.";
        if (error.Contains("not installed") || error.Contains("no longer installed")) return "Раскладка недоступна. Проверьте языки Windows.";
        if (error.Contains("at least two") || error.Contains("exactly two")) return "Добавьте и выберите два основных языка.";
        if (error.Contains("shortcut", StringComparison.OrdinalIgnoreCase)) return "Выберите Ctrl, Alt или Shift с буквой, цифрой или пробелом.";
        if (error.Contains("could not load")) return "Windows не смогла загрузить раскладку. Проверьте её в настройках языков.";
        if (error.StartsWith("Could not read settings")) return "Не удалось прочитать настройки. Исходный файл сохранён; выберите языки заново.";
        return error;
    }
}
