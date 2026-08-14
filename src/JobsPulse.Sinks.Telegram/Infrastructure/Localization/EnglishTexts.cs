namespace JobsPulse.Sinks.Telegram.Infrastructure.Localization;

/// <summary>English side of the text table. Placeholders are <c>{0}</c>-style and documented by the key name.</summary>
internal static class EnglishTexts
{
    internal static readonly string[] Months =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    internal static readonly Dictionary<TextKey, string> Values = new()
    {
        [TextKey.MenuTitle] = "Main menu",
        [TextKey.MenuGreeting] =
            "I watch company career pages and tell you when a matching vacancy appears.<br>"
            + "A <b>watchlist</b> is a set of companies plus one filter. Create one, add companies, and I will keep "
            + "an eye on them.",
        [TextKey.MenuMyWatchlists] = "📋 My watchlists",
        [TextKey.MenuAllWatchlists] = "🌍 All watchlists",
        [TextKey.MenuVacancies] = "💼 Vacancies",
        [TextKey.MenuDisabledCompanies] = "⏸ Disabled companies",
        [TextKey.MenuLanguage] = "🌐 Language",
        [TextKey.MenuAdmin] = "🛠 Admin",
        [TextKey.MenuHelp] = "❓ How it works",

        [TextKey.Back] = "⬅ Back",
        [TextKey.ToMenu] = "🏠 Menu",
        [TextKey.PrevPage] = "‹",
        [TextKey.NextPage] = "›",
        [TextKey.Page] = "page {0} of {1}",

        [TextKey.MyWatchlistsTitle] = "My watchlists",
        [TextKey.MyWatchlistsEmpty] =
            "You have no watchlists yet. Create the first one and add the companies you care about.",
        [TextKey.AllWatchlistsTitle] = "All watchlists",
        [TextKey.AllWatchlistsHint] =
            "Other people's watchlists are shown as examples — you can look inside, but only your own can be edited.",
        [TextKey.WatchlistOwnerYou] = "you",
        [TextKey.WatchlistOwnerSystem] = "system",
        [TextKey.WatchlistOwnerOther] = "someone else",
        [TextKey.WatchlistCreate] = "➕ New watchlist",
        [TextKey.WatchlistCreatePrompt] =
            "Send a name for the new watchlist, for example <b>Backend Europe</b>.",
        [TextKey.WatchlistCreated] = "Watchlist «{0}» is created. Now add the companies you want to watch.",
        [TextKey.WatchlistNameTaken] = "The name «{0}» is already taken. Try another one.",
        [TextKey.WatchlistNameTooLong] = "That name is too long — up to {0} characters, please.",
        [TextKey.WatchlistReadOnly] = "This watchlist belongs to somebody else, so it is read-only for you.",
        [TextKey.WatchlistGone] = "That watchlist no longer exists.",

        [TextKey.WatchlistTitle] = "{0}",
        [TextKey.WatchlistStateActive] = "watching",
        [TextKey.WatchlistStatePaused] = "paused",
        [TextKey.WatchlistFilterLabel] = "Filter",
        [TextKey.WatchlistCompaniesLabel] = "Companies",
        [TextKey.WatchlistMatchesLabel] = "Matching vacancies",
        [TextKey.WatchlistRename] = "✏️ Rename",
        [TextKey.WatchlistRenamePrompt] = "Send the new name for «{0}».",
        [TextKey.WatchlistRenamed] = "Renamed to «{0}».",
        [TextKey.WatchlistOpenVacancies] = "💼 Vacancies",
        [TextKey.WatchlistOpenCompanies] = "🏢 Companies",
        [TextKey.WatchlistEditFilter] = "🔧 Filter",
        [TextKey.WatchlistAddCompany] = "➕ Add company",
        [TextKey.WatchlistPause] = "⏸ Pause",
        [TextKey.WatchlistResume] = "▶️ Resume",
        [TextKey.WatchlistDelete] = "🗑 Delete",
        [TextKey.WatchlistDeleteConfirm] =
            "Delete «{0}» with all its companies? The vacancies already found are kept.",
        [TextKey.WatchlistDeleted] = "Watchlist «{0}» is deleted.",
        [TextKey.ConfirmYes] = "✅ Yes",
        [TextKey.ConfirmNo] = "✖ No",

        [TextKey.FilterTitle] = "Filter of «{0}»",
        [TextKey.FilterEmpty] = "No filter yet — every vacancy of these companies counts as a match.",
        [TextKey.FilterKeywords] = "🔍 Title keywords",
        [TextKey.FilterExcluded] = "🚫 Excluded words",
        [TextKey.FilterLocations] = "📍 Locations",
        [TextKey.FilterLocationsExcluded] = "🚫 Excluded locations",
        [TextKey.FilterDescription] = "📝 Words in the text",
        [TextKey.FilterDescriptionExcluded] = "🚫 Excluded in the text",
        [TextKey.FilterFreshness] = "🗓 Freshness",
        [TextKey.FilterKeywordsPrompt] =
            "Send the words a vacancy title must contain, comma separated — for example <b>backend, sre, platform</b>. "
            + "A vacancy matching any of them is a hit. Send <b>-</b> to clear.",
        [TextKey.FilterExcludedPrompt] =
            "Send the words that must <b>not</b> appear in the title, comma separated — for example "
            + "<b>intern, sales</b>. Send <b>-</b> to clear.",
        [TextKey.FilterLocationsPrompt] =
            "Send the locations you accept, comma separated — for example <b>remote, berlin, poland</b>. "
            + "Send <b>-</b> to clear.",
        [TextKey.FilterLocationsExcludedPrompt] =
            "Send the locations you do <b>not</b> want, comma separated — for example <b>usa, india</b>. "
            + "A vacancy named after any of them is dropped, whatever the other rules say. Send <b>-</b> to clear.",
        [TextKey.FilterDescriptionPrompt] =
            "Send the words the vacancy <b>text</b> must contain, comma separated — for example "
            + "<b>kubernetes, postgres</b>. A vacancy matching any of them is a hit. Send <b>-</b> to clear.<br>"
            + "Two things to know: a vacancy whose text I could not read never matches such a rule, and the rule is "
            + "checked when the company is polled — vacancies found earlier are not re-checked against it.",
        [TextKey.FilterDescriptionExcludedPrompt] =
            "Send the words that must <b>not</b> appear in the vacancy text, comma separated — for example "
            + "<b>on-site, security clearance</b>. Send <b>-</b> to clear.<br>"
            + "As above: a vacancy with no readable text passes this rule, and already found vacancies are not "
            + "re-checked against it.",
        [TextKey.FilterFreshnessPrompt] = "How old may a vacancy be?",
        [TextKey.FilterFreshnessAny] = "Any age",
        [TextKey.FilterClear] = "🧹 Clear filter",
        [TextKey.FilterSaved] = "Filter updated. Stored vacancies are re-checked on the next round.",
        [TextKey.FilterCleared] = "Filter cleared — every vacancy of these companies is a match now.",
        [TextKey.FilterAnyValue] = "any",
        [TextKey.FilterDays] = "last {0} days",

        [TextKey.CompaniesTitle] = "Companies of «{0}»",
        [TextKey.CompaniesEmpty] = "No companies here yet. Add the first one.",
        [TextKey.CompanyStatusActive] = "watching",
        [TextKey.CompanyStatusDisabled] = "disabled",
        [TextKey.CompanyStatusWorked] = "worked through",
        [TextKey.CompanyLegend] = "▶️ watching · ✅ worked through · ⏸ disabled",
        [TextKey.CompanyMarkWorked] = "✅ Mark as worked through",
        [TextKey.CompanyUnmarkWorked] = "↩️ Not worked through",
        [TextKey.CompanyDisable] = "⏸ Disable",
        [TextKey.CompanyEnable] = "▶️ Enable",
        [TextKey.CompanyRemove] = "🗑 Remove",
        [TextKey.CompanyMarkedWorked] = "«{0}» is marked as worked through.",
        [TextKey.CompanyUnmarkedWorked] = "«{0}» is no longer marked as worked through.",
        [TextKey.CompanyDisabled] = "«{0}» is disabled — I stop watching it.",
        [TextKey.CompanyEnabled] = "«{0}» is active again.",
        [TextKey.CompanyRemoved] = "«{0}» is removed.",
        [TextKey.CompanyDisabledInsteadOfRemoved] =
            "«{0}» was found automatically, so it is kept disabled instead of removed — otherwise it would come back "
            + "on the next sweep.",
        [TextKey.CompanyWorkedOn] = "CV sent {0}",
        [TextKey.CompanyFoundByDiscovery] = "found automatically",
        [TextKey.CompanyChange] = "🔧 Change a company",
        [TextKey.CompanyFindPrompt] =
            "Send the name of the company you want to change. Part of the name is enough.",
        [TextKey.CompanyFindNotFound] = "No company «{0}» in this list. Try another name.",
        [TextKey.CompanyFindMany] = "Several companies match «{0}» — pick one.",
        [TextKey.CompanyCounts] = "{0}",
        [TextKey.CompanyCountsLegend] = "After every company: vacancies found on its board matching this filter",
        [TextKey.CompaniesBySource] = "🏢 Group by source",
        [TextKey.CompaniesByLocation] = "📍 Group by location",

        [TextKey.DisabledTitle] = "Disabled companies",
        [TextKey.DisabledEmpty] = "Nothing is disabled — every company of yours is being watched.",
        [TextKey.DisabledHint] = "Tap a company to start watching it again.",

        [TextKey.AddCompanyPrompt] =
            "Send a company name, for example <b>Nebius</b> — or a link to its careers page.",
        [TextKey.AddCompanySearching] = "Looking for «{0}»…",
        [TextKey.AddCompanyNotFound] =
            "I could not find «{0}». Try the exact name, or send a link to the careers page.",
        [TextKey.AddCompanyAlready] = "«{0}» is already in this watchlist.",
        [TextKey.AddCompanyAdded] = "«{0}» is added. I will report its changes from now on.",
        [TextKey.AddCompanyChoose] = "Which one do you mean?",
        [TextKey.AddCompanyVacancies] = "{0} vacancies",

        [TextKey.VacanciesTitle] = "Vacancies of «{0}»",
        [TextKey.VacanciesPickWatchlist] = "Pick a watchlist to see what was found for it.",
        [TextKey.VacanciesEmpty] =
            "Nothing found yet. Either the companies have no matching openings, or the first round is still running.",
        [TextKey.VacanciesCount] = "{0} open vacancies match this watchlist.",
        [TextKey.VacanciesShownOf] = "Showing the {0} freshest vacancies out of {1}.",
        [TextKey.VacancyUnknownLocation] = "Unknown location",
        [TextKey.VacanciesByCompany] = "🏢 Group by company",
        [TextKey.VacanciesByLocation] = "📍 Group by location",

        [TextKey.RegionEurope] = "Europe",
        [TextKey.RegionRemote] = "Remote",
        [TextKey.RegionCis] = "CIS",
        [TextKey.RegionAmericas] = "Americas",
        [TextKey.RegionAsia] = "Asia",
        [TextKey.RegionMiddleEastAndAfrica] = "Middle East and Africa",
        [TextKey.RegionOceania] = "Australia and Oceania",
        [TextKey.RegionUnknown] = "Location unclear",

        [TextKey.LanguageTitle] = "Choose a language",
        [TextKey.LanguageChanged] = "Language switched to English.",

        [TextKey.Help] =
            "<b>How it works</b><br>"
            + "1. Create a watchlist — a named set of companies.<br>"
            + "2. Add companies by name or by a link to their careers page.<br>"
            + "3. Set a filter so only the vacancies you care about reach you.<br>"
            + "4. I check the companies regularly and send new, changed and closed vacancies.<br><br>"
            + "Mark a company as ✅ worked through once you have sent your CV, and it will stand out in the list. "
            + "Companies you are not interested in right now can be disabled and brought back later.",
        [TextKey.UnknownCommand] = "I did not understand that. Here is the menu.",
        [TextKey.SessionExpired] = "That step has expired — start again from the menu.",
        [TextKey.NotAllowed] = "You cannot change this.",
        [TextKey.AdminOnly] = "This part is for administrators only.",
        [TextKey.SomethingWentWrong] = "Something went wrong. Try again in a moment.",
        [TextKey.Saved] = "Saved.",
        [TextKey.Nothing] = "—",

        [TextKey.NotificationNew] = "New",
        [TextKey.NotificationUpdated] = "Changed",
        [TextKey.NotificationClosed] = "Closed",
        [TextKey.NotificationNewBoard] = "New company found"
    };
}
