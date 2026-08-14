namespace JobsPulse.Sinks.Telegram.Models;

/// <summary>
/// What a button does. Serialized by its short code, never by name: telegram caps callback data at 64 bytes and it
/// still has to carry an id and a page number.
/// </summary>
public enum CallbackAction
{
    None,

    Menu,
    Help,
    MyWatchlists,
    AllWatchlists,
    Language,
    SetLanguage,
    Admin,

    WatchlistNew,
    WatchlistOpen,
    WatchlistRename,
    WatchlistTogglePaused,
    WatchlistDeleteAsk,
    WatchlistDelete,

    FilterOpen,
    FilterKeywords,
    FilterExcluded,
    FilterLocations,
    FilterLocationsExcluded,
    FilterDescription,
    FilterDescriptionExcluded,
    FilterFreshnessAsk,
    FilterFreshnessSet,
    FilterClear,

    CompaniesOpen,

    /// <summary>The same list grouped by region instead of by source - the grouping is the action, not a flag.</summary>
    CompaniesByLocation,

    CompanyOpen,
    CompanyToggleWorked,
    CompanyToggleEnabled,
    CompanyRemove,
    CompanyAdd,
    CompanyPick,

    /// <summary>Asks for a company name instead of putting a button on every row of a long list.</summary>
    CompanyFind,

    DisabledCompanies,

    /// <summary>Re-enables a company and returns to the disabled list, not to the watchlist it belongs to.</summary>
    CompanyRestore,

    VacanciesPick,
    VacanciesOpen,

    /// <summary>The feed grouped by region, Europe first, instead of by company.</summary>
    VacanciesByLocation
}
