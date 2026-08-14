namespace JobsPulse.Sinks.Telegram.Infrastructure.Localization;

/// <summary>
/// Every user-facing string of the bot. An enum rather than loose string keys so a missing translation is a compile
/// error on the table, not a blank message at runtime.
/// </summary>
public enum TextKey
{
    // Main menu
    MenuTitle,
    MenuGreeting,
    MenuMyWatchlists,
    MenuAllWatchlists,
    MenuVacancies,
    MenuDisabledCompanies,
    MenuLanguage,
    MenuAdmin,
    MenuHelp,

    // Navigation
    Back,
    ToMenu,
    PrevPage,
    NextPage,
    Page,

    // Watchlists
    MyWatchlistsTitle,
    MyWatchlistsEmpty,
    AllWatchlistsTitle,
    AllWatchlistsHint,
    WatchlistOwnerYou,
    WatchlistOwnerSystem,
    WatchlistOwnerOther,
    WatchlistCreate,
    WatchlistCreatePrompt,
    WatchlistCreated,
    WatchlistNameTaken,
    WatchlistNameTooLong,
    WatchlistReadOnly,
    WatchlistGone,

    // One watchlist
    WatchlistTitle,
    WatchlistStateActive,
    WatchlistStatePaused,
    WatchlistFilterLabel,
    WatchlistCompaniesLabel,
    WatchlistMatchesLabel,
    WatchlistRename,
    WatchlistRenamePrompt,
    WatchlistRenamed,
    WatchlistOpenVacancies,
    WatchlistOpenCompanies,
    WatchlistEditFilter,
    WatchlistAddCompany,
    WatchlistPause,
    WatchlistResume,
    WatchlistDelete,
    WatchlistDeleteConfirm,
    WatchlistDeleted,
    ConfirmYes,
    ConfirmNo,

    // Filter
    FilterTitle,
    FilterEmpty,
    FilterKeywords,
    FilterExcluded,
    FilterLocations,
    FilterLocationsExcluded,
    FilterDescription,
    FilterDescriptionExcluded,
    FilterFreshness,
    FilterKeywordsPrompt,
    FilterExcludedPrompt,
    FilterLocationsPrompt,
    FilterLocationsExcludedPrompt,
    FilterDescriptionPrompt,
    FilterDescriptionExcludedPrompt,
    FilterFreshnessPrompt,
    FilterFreshnessAny,
    FilterClear,
    FilterSaved,
    FilterCleared,
    FilterAnyValue,
    FilterDays,

    // Companies
    CompaniesTitle,
    CompaniesEmpty,
    CompanyStatusActive,
    CompanyStatusDisabled,
    CompanyStatusWorked,
    CompanyLegend,
    CompanyMarkWorked,
    CompanyUnmarkWorked,
    CompanyDisable,
    CompanyEnable,
    CompanyRemove,
    CompanyMarkedWorked,
    CompanyUnmarkedWorked,
    CompanyDisabled,
    CompanyEnabled,
    CompanyRemoved,
    CompanyDisabledInsteadOfRemoved,
    CompanyWorkedOn,
    CompanyFoundByDiscovery,
    CompanyChange,
    CompanyFindPrompt,
    CompanyFindNotFound,
    CompanyFindMany,
    CompanyCounts,
    CompanyCountsLegend,
    CompaniesBySource,
    CompaniesByLocation,

    // Disabled companies screen
    DisabledTitle,
    DisabledEmpty,
    DisabledHint,

    // Add company
    AddCompanyPrompt,
    AddCompanySearching,
    AddCompanyNotFound,
    AddCompanyAlready,
    AddCompanyAdded,
    AddCompanyChoose,
    AddCompanyVacancies,

    // Vacancies
    VacanciesTitle,
    VacanciesPickWatchlist,
    VacanciesEmpty,
    VacanciesCount,
    VacanciesShownOf,
    VacancyUnknownLocation,
    VacanciesByCompany,
    VacanciesByLocation,

    // Regions - the order of the enum is the order they are shown in
    RegionEurope,
    RegionRemote,
    RegionCis,
    RegionAmericas,
    RegionAsia,
    RegionMiddleEastAndAfrica,
    RegionOceania,
    RegionUnknown,

    // Language
    LanguageTitle,
    LanguageChanged,

    // Errors and generic
    Help,
    UnknownCommand,
    SessionExpired,
    NotAllowed,
    AdminOnly,
    SomethingWentWrong,
    Saved,
    Nothing,

    // Notification headers
    NotificationNew,
    NotificationUpdated,
    NotificationClosed,
    NotificationNewBoard
}
