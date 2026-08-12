using System.Collections.Frozen;

namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// The payload behind an inline button: <c>"wo:12:3"</c> - action code, an id and a page. Telegram allows 64 bytes,
/// hence the two-letter codes instead of enum names.
/// </summary>
/// <param name="Id">Watchlist id, entry id or a list index - whatever the action addresses.</param>
/// <param name="Page">Page of the list the button was rendered on, so «back» returns to the same page.</param>
public readonly record struct CallbackData(CallbackAction Action, long Id = 0, int Page = 0)
{
    private const char Separator = ':';

    private static readonly FrozenDictionary<CallbackAction, string> Codes = new Dictionary<CallbackAction, string>
    {
        [CallbackAction.Menu] = "m",
        [CallbackAction.Help] = "h",
        [CallbackAction.MyWatchlists] = "mw",
        [CallbackAction.AllWatchlists] = "aw",
        [CallbackAction.Language] = "l",
        [CallbackAction.SetLanguage] = "ls",
        [CallbackAction.Admin] = "ad",

        [CallbackAction.WatchlistNew] = "wn",
        [CallbackAction.WatchlistOpen] = "wo",
        [CallbackAction.WatchlistRename] = "wr",
        [CallbackAction.WatchlistTogglePaused] = "wp",
        [CallbackAction.WatchlistDeleteAsk] = "wda",
        [CallbackAction.WatchlistDelete] = "wd",

        [CallbackAction.FilterOpen] = "fo",
        [CallbackAction.FilterKeywords] = "fk",
        [CallbackAction.FilterExcluded] = "fx",
        [CallbackAction.FilterLocations] = "fl",
        [CallbackAction.FilterFreshnessAsk] = "ffa",
        [CallbackAction.FilterFreshnessSet] = "ffs",
        [CallbackAction.FilterClear] = "fc",

        [CallbackAction.CompaniesOpen] = "co",
        [CallbackAction.CompanyOpen] = "ce",
        [CallbackAction.CompanyToggleWorked] = "cw",
        [CallbackAction.CompanyToggleEnabled] = "cn",
        [CallbackAction.CompanyRemove] = "cr",
        [CallbackAction.CompanyAdd] = "ca",
        [CallbackAction.CompanyPick] = "cp",

        [CallbackAction.DisabledCompanies] = "dc",
        [CallbackAction.CompanyRestore] = "cs",

        [CallbackAction.VacanciesPick] = "vp",
        [CallbackAction.VacanciesOpen] = "vo"
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, CallbackAction> Actions =
        Codes.ToFrozenDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

    public override string ToString() =>
        $"{Codes.GetValueOrDefault(Action, "?")}{Separator}{Id}{Separator}{Page}";

    /// <summary>Anything unparsable becomes <see cref="CallbackAction.None"/> - a stale button must not throw.</summary>
    public static CallbackData Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new CallbackData(CallbackAction.None);

        var parts = raw.Split(Separator);

        if (!Actions.TryGetValue(parts[0], out var action))
            return new CallbackData(CallbackAction.None);

        var id = parts.Length > 1 && long.TryParse(parts[1], out var parsedId) ? parsedId : 0;
        var page = parts.Length > 2 && int.TryParse(parts[2], out var parsedPage) ? parsedPage : 0;

        return new CallbackData(action, id, page);
    }
}
