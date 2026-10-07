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
        [TextKey.MenuSilentOn] = "🔕 Silent mode: on",
        [TextKey.MenuSilentOff] = "🔔 Silent mode: off",
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
        [TextKey.WatchlistStats] = "📊 Statistics",
        [TextKey.WatchlistShortlist] = "🎯 Shortlist",
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
        [TextKey.FilterTitleButton] = "🔍 Title",
        [TextKey.FilterLocationButton] = "📍 Location",
        [TextKey.FilterTextButton] = "📝 Vacancy text",
        [TextKey.FilterKeywordsPrompt] =
            "Words of the vacancy <b>title</b>. Wanted ones as they are, excluded ones with a minus: "
            + "<b>backend, sre, -intern, -manager</b>. Any wanted word is a hit, any excluded one drops the vacancy.",
        [TextKey.FilterLocationsPrompt] =
            "<b>Locations</b>. Accepted ones as they are, unwanted ones with a minus: <b>remote, berlin, -usa, -india</b>. "
            + "A vacancy in an excluded location is dropped, whatever the other rules say.",
        [TextKey.FilterDescriptionPrompt] =
            "Words of the vacancy <b>text</b>. Wanted ones as they are, excluded ones with a minus: "
            + "<b>kubernetes, postgres, -on-site, -security clearance</b>.<br>"
            + "The text is read when the company is polled: vacancies found earlier are not re-checked against this "
            + "rule, and a vacancy whose text could not be read never matches wanted words and passes excluded ones.",
        [TextKey.FilterFreshnessPrompt] = "How old may a vacancy be?",
        [TextKey.FilterFreshnessAny] = "Any age",
        [TextKey.FilterClear] = "🧹 Clear filter",
        [TextKey.FilterCurrent] =
            "Now (tap to copy): <code>{0}</code>",
        [TextKey.FilterCurrentEmpty] = "The rule is empty now.",
        [TextKey.FilterEditModes] =
            "Words are <b>added</b> to the current ones; a word already in the other list moves. "
            + "Start the answer with <b>=</b> to replace the whole field — copy the current value below, edit it "
            + "and send it. A lone <b>-</b> clears the field.",
        [TextKey.FilterUnchanged] = "The rule is unchanged — it already holds these words.",
        [TextKey.FilterSaved] = "Filter updated. Stored vacancies are re-checked on the next round.",
        [TextKey.FilterCleared] = "Filter cleared — every vacancy of these companies is a match now.",
        [TextKey.FilterAnyValue] = "any",
        [TextKey.FilterDays] = "last {0} days",

        [TextKey.CompaniesTitle] = "Companies of «{0}»",
        [TextKey.CompaniesEmpty] = "No companies here yet. Add the first one.",
        [TextKey.CompanyStatusActive] = "watching",
        [TextKey.CompanyStatusDisabled] = "disabled",
        [TextKey.CompanyStatusWorked] = "worked through",
        [TextKey.CompaniesDisabledCount] = "⏸ Disabled companies: <b>{0}</b> — they are on the disabled screen.",
        [TextKey.CompaniesAllDisabled] = "Every company of this watchlist is disabled.",
        [TextKey.CompanyLegend] = "▶️ watching · ✅ worked through",
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
        // Short on purpose: the four groupings share one keyboard row.
        [TextKey.CompaniesBySource] = "🏢 By source",
        [TextKey.CompaniesByLocation] = "📍 By location",
        [TextKey.CompaniesByMonth] = "🗓 By month",
        [TextKey.CompaniesByActivity] = "🔥 By activity",

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
        [TextKey.VacanciesByCompany] = "🏢 By company",
        [TextKey.VacanciesByLocation] = "📍 By location",
        [TextKey.VacanciesByMonth] = "🗓 By month",
        [TextKey.VacanciesByActivity] = "🔥 By activity",

        [TextKey.MonthUnknown] = "Date unknown",

        [TextKey.ActivityBlazing] = "Boiling",
        [TextKey.ActivityHot] = "Busy",
        [TextKey.ActivityWarm] = "Slow",
        [TextKey.ActivityStill] = "Nothing moved",
        [TextKey.ActivityRate] = "{0}/mo",
        [TextKey.ActivityBreakdown] = "+{0} / ✏️{1} / ❌{2}",
        [TextKey.ActivityLegend] =
            "Activity: vacancy events per month on the company board — opened / changed / closed. "
            + "An impossible number usually means the board rewrites its postings, not that it is hiring.",

        [TextKey.RegionEurope] = "Europe",
        [TextKey.RegionRemote] = "Remote",
        [TextKey.RegionCis] = "CIS",
        [TextKey.RegionAmericas] = "Americas",
        [TextKey.RegionAsia] = "Asia",
        [TextKey.RegionMiddleEastAndAfrica] = "Middle East and Africa",
        [TextKey.RegionOceania] = "Australia and Oceania",
        [TextKey.RegionUnknown] = "Location unclear",

        [TextKey.StatsTitle] = "📊 {0} · last {1}",
        [TextKey.StatsDigestTitle] = "📊 Digest for the last {1} · {0}",
        [TextKey.StatsDigestSinceTitle] = "📊 What changed since the last digest · {0}",
        [TextKey.StatsOpenVacancies] = "📦 Open vacancies: <b>{0}</b> (were {1}, {2})",
        [TextKey.StatsOpenCompanies] = "🏢 Companies with vacancies: <b>{0}</b> (were {1}, {2})",
        [TextKey.StatsPeriod] = "{0} – {1} UTC",
        [TextKey.StatsDaysOne] = "{0} day",
        [TextKey.StatsDaysFew] = "{0} days",
        [TextKey.StatsDaysMany] = "{0} days",
        [TextKey.StatsOpened] = "🆕 Vacancies opened: <b>{0}</b>",
        [TextKey.StatsClosed] = "❌ Vacancies closed: <b>{0}</b>",
        [TextKey.StatsOpenedThenEnded] = ", {0} of them already ended",
        [TextKey.StatsDropped] = "🧹 Aged out or filtered away: <b>{0}</b>",
        [TextKey.StatsNewCompanies] = "🆕 New companies with vacancies: <b>{0}</b>",
        [TextKey.StatsNewThenEmptied] = ", {0} of them already without vacancies",
        [TextKey.StatsEmptiedCompanies] = "🏁 Companies left without vacancies: <b>{0}</b>",
        [TextKey.StatsTopActivity] = "🔥 Most active companies",
        [TextKey.StatsTopOpened] = "📈 Most new vacancies",
        [TextKey.StatsActivityRow] = "{0} events ({1})",
        [TextKey.StatsOpenedRow] = "{0} new",
        [TextKey.StatsNothing] = "nothing in this period",
        [TextKey.StatsDaysButton] = "{0} days",
        [TextKey.StatsCustom] = "✏️ Other period",
        [TextKey.StatsCustomPrompt] = "How many days should the statistics cover? Send a number from 1 to {0}.",
        [TextKey.StatsCustomInvalid] = "That is not a number of days from 1 to {0}. Send another one.",

        [TextKey.AreaWesternEurope] = "🇪🇺 Western Europe",
        [TextKey.AreaEasternEurope] = "🇪🇺 Eastern Europe",
        [TextKey.AreaUsa] = "🇺🇸 USA",
        [TextKey.AreaAsia] = "🌏 Asia",
        [TextKey.ShortlistTitle] = "🎯 Shortlist · {0}",
        [TextKey.ShortlistFresh] = "Vacancies published in the last {0}.",
        [TextKey.ShortlistRegion] = "Vacancies in: {0}.",
        [TextKey.ShortlistHint] = "Top {0} companies by matching vacancies (disabled ones are left out), {1} best to apply to in each: the most title keywords of the filter, then the freshest. A company name opens all its vacancies.",
        [TextKey.ShortlistCompanyRow] = "{0} matching",
        [TextKey.ShortlistWorked] = "✅ worked through",
        [TextKey.ShortlistEmpty] = "Nothing matches in this slice.",
        [TextKey.ShortlistCustomPrompt] = "Vacancies of how many last days? Send a number from 1 to {0}.",
        [TextKey.CompanyVacanciesTitle] = "🏢 {0} · {1}",
        [TextKey.CompanyVacanciesEmpty] = "No matching vacancy of this company is open now.",
        [TextKey.CompanyVacanciesAll] = "📋 All vacancies of the watchlist",

        [TextKey.RunTitlePolling] = "🔄 Polling run · {0}",
        [TextKey.RunTitleRegistry] = "🗂 Registry run · {0}",
        [TextKey.RunPeriod] = "⏱ Run: {0}",
        [TextKey.ChangesPeriod] = "🗓 Changes: {0}",
        [TextKey.ChangesFirstPoll] = "🗓 Changes: every company was polled for the first time",
        [TextKey.DiscoveryCollections] = "🗓 Crawl indexes: {0}",
        [TextKey.RunWalked] = "Boards walked: <b>{0}</b>, failed: {1}, changes: {2}",
        [TextKey.RunUnfinished] = "The run stopped before it finished — the numbers below are what it committed.",
        [TextKey.DiscoveryTitle] = "🔎 Discovery run",
        [TextKey.DiscoveryTitleFull] = "🔎 Discovery run (full)",
        [TextKey.DiscoveryCounts] =
            "Crawl indexes: <b>{0}</b> (failed {1}, left {2})<br>Records read: {3}<br>"
            + "Board tokens: {4}, validated: {5}<br>New boards in the registry: <b>{6}</b>",

        [TextKey.LanguageTitle] = "Choose a language",
        [TextKey.LanguageChanged] = "Language switched to English.",

        [TextKey.DigestAllChanges] = "📋 All changes",
        [TextKey.DigestOpenedButton] = "🆕 Opened",
        [TextKey.DigestClosedButton] = "❌ Closed",
        [TextKey.DigestOpenedTitle] = "🆕 Opened vacancies · {0}",
        [TextKey.DigestClosedTitle] = "❌ Closed vacancies · {0}",
        [TextKey.DigestChangesTitle] = "📋 Changes · {0}",
        [TextKey.DigestChangesEmpty] = "Nothing changed in this period.",
        [TextKey.DigestNewCompanies] = "🏢 New companies: <b>{0}</b>",
        [TextKey.DigestEmptiedCompanies] = "🏁 Companies left without vacancies: <b>{0}</b>",
        [TextKey.DigestCompanyRow] = "🆕 {0} · ❌ {1} · net {2}",
        [TextKey.DigestUnknownTitle] = "vacancy {0}",

        [TextKey.SilentModeOn] = "Silent mode is on: no reports after runs and no vacancy notifications. Only the digest arrives.",
        [TextKey.SilentModeOff] = "Silent mode is off: vacancies and a report after every run.",

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
        [TextKey.NotificationNewBoard] = "New company found",
        [TextKey.NotificationWindow] = "🕔 {0}, {1}–{2} UTC",
        [TextKey.NotificationWindowCounts] = "{0} changes in {1} companies"
    };
}
