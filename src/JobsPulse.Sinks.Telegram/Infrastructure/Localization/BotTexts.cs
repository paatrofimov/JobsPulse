using System.Collections.Frozen;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Sinks.Telegram.Infrastructure.Localization;

/// <summary>
/// The whole user-facing vocabulary of the bot in one place: menus, buttons, hints, statuses, errors and the
/// notification headers.
///
/// Deliberately a hand-written table rather than resx or <c>CultureInfo</c>: the solution builds with
/// <c>InvariantGlobalization=true</c>, under which culture-based resource and date lookups silently fall back to
/// English - month names included. Here both languages are explicit, so a missing translation is visible.
/// </summary>
public static class BotTexts
{
    private static readonly FrozenDictionary<TextKey, string> English =
        EnglishTexts.Values.ToFrozenDictionary();

    private static readonly FrozenDictionary<TextKey, string> Russian =
        RussianTexts.Values.ToFrozenDictionary();

    public static string Get(TextKey key, BotLanguage language)
    {
        var table = Table(language);

        // English is the fallback, and the key itself is the last resort - a blank message tells nobody anything.
        return table.TryGetValue(key, out var value)
            ? value
            : English.GetValueOrDefault(key, key.ToString());
    }

    public static string Get(TextKey key, BotLanguage language, params object?[] args) =>
        string.Format(Get(key, language), args);

    /// <summary>«12 August» / «12 августа» - month names come from the table, not from a culture.</summary>
    public static string FormatDate(DateTimeOffset date, bool withYear, BotLanguage language)
    {
        var months = language == BotLanguage.Russian ? RussianTexts.Months : EnglishTexts.Months;
        var month = months[date.Month - 1];

        var day = language == BotLanguage.Russian
            ? $"{date.Day} {month}"
            : $"{month} {date.Day:00}";

        return withYear ? $"{day} {date.Year}" : day;
    }

    public static string LanguageName(BotLanguage language) => language switch
    {
        BotLanguage.Russian => "🇷🇺 Русский",
        _ => "🇬🇧 English"
    };

    /// <summary>The two-letter code `setMyCommands` takes, so the command menu is localized too.</summary>
    public static string LanguageCode(BotLanguage language) => language switch
    {
        BotLanguage.Russian => "ru",
        _ => "en"
    };

    /// <summary>Best guess from the telegram client locale, used only for a user we have never seen.</summary>
    public static BotLanguage FromTelegramCode(string? languageCode) =>
        languageCode is not null && languageCode.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? BotLanguage.Russian
            : BotLanguage.English;

    private static FrozenDictionary<TextKey, string> Table(BotLanguage language) =>
        language == BotLanguage.Russian ? Russian : English;
}
