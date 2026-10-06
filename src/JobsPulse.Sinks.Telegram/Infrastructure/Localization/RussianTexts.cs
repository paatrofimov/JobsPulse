namespace JobsPulse.Sinks.Telegram.Infrastructure.Localization;

/// <summary>Russian side of the text table. Must hold every <see cref="TextKey"/> - see <see cref="BotTexts"/>.</summary>
internal static class RussianTexts
{
    internal static readonly string[] Months =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];

    /// <summary>«12 сентября» needs the genitive, «Сентябрь 2026» needs the nominative - two tables, one language.</summary>
    internal static readonly string[] MonthsNominative =
    [
        "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
        "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
    ];

    internal static readonly Dictionary<TextKey, string> Values = new()
    {
        [TextKey.MenuTitle] = "Главное меню",
        [TextKey.MenuGreeting] =
            "Я слежу за страницами вакансий компаний и сообщаю, когда появляется подходящая.<br>"
            + "<b>Список наблюдения</b> — это набор компаний и один фильтр. Создайте список, добавьте компании, "
            + "и я буду за ними присматривать.",
        [TextKey.MenuMyWatchlists] = "📋 Мои списки",
        [TextKey.MenuAllWatchlists] = "🌍 Все списки",
        [TextKey.MenuVacancies] = "💼 Вакансии",
        [TextKey.MenuDisabledCompanies] = "⏸ Отключённые компании",
        [TextKey.MenuLanguage] = "🌐 Язык",
        [TextKey.MenuSilentOn] = "🔕 Тихий режим: вкл",
        [TextKey.MenuSilentOff] = "🔔 Тихий режим: выкл",
        [TextKey.MenuAdmin] = "🛠 Администрирование",
        [TextKey.MenuHelp] = "❓ Как это работает",

        [TextKey.Back] = "⬅ Назад",
        [TextKey.ToMenu] = "🏠 Меню",
        [TextKey.PrevPage] = "‹",
        [TextKey.NextPage] = "›",
        [TextKey.Page] = "страница {0} из {1}",

        [TextKey.MyWatchlistsTitle] = "Мои списки наблюдения",
        [TextKey.MyWatchlistsEmpty] =
            "У вас пока нет списков наблюдения. Создайте первый и добавьте интересные компании.",
        [TextKey.AllWatchlistsTitle] = "Все списки наблюдения",
        [TextKey.AllWatchlistsHint] =
            "Чужие списки показаны как примеры — заглянуть внутрь можно, но менять получится только свои.",
        [TextKey.WatchlistOwnerYou] = "вы",
        [TextKey.WatchlistOwnerSystem] = "системный",
        [TextKey.WatchlistOwnerOther] = "другой пользователь",
        [TextKey.WatchlistCreate] = "➕ Новый список",
        [TextKey.WatchlistCreatePrompt] =
            "Пришлите название нового списка, например <b>Backend Европа</b>.",
        [TextKey.WatchlistCreated] = "Список «{0}» создан. Теперь добавьте компании, за которыми стоит следить.",
        [TextKey.WatchlistNameTaken] = "Название «{0}» уже занято. Попробуйте другое.",
        [TextKey.WatchlistNameTooLong] = "Слишком длинное название — не больше {0} символов.",
        [TextKey.WatchlistReadOnly] = "Этот список принадлежит другому пользователю, поэтому доступен только для чтения.",
        [TextKey.WatchlistGone] = "Такого списка больше нет.",

        [TextKey.WatchlistTitle] = "{0}",
        [TextKey.WatchlistStateActive] = "следим",
        [TextKey.WatchlistStatePaused] = "на паузе",
        [TextKey.WatchlistFilterLabel] = "Фильтр",
        [TextKey.WatchlistCompaniesLabel] = "Компании",
        [TextKey.WatchlistMatchesLabel] = "Подходящих вакансий",
        [TextKey.WatchlistRename] = "✏️ Переименовать",
        [TextKey.WatchlistRenamePrompt] = "Пришлите новое название для «{0}».",
        [TextKey.WatchlistRenamed] = "Новое название — «{0}».",
        [TextKey.WatchlistOpenVacancies] = "💼 Вакансии",
        [TextKey.WatchlistOpenCompanies] = "🏢 Компании",
        [TextKey.WatchlistStats] = "📊 Статистика",
        [TextKey.WatchlistShortlist] = "🎯 Выжимка",
        [TextKey.WatchlistEditFilter] = "🔧 Фильтр",
        [TextKey.WatchlistAddCompany] = "➕ Добавить компанию",
        [TextKey.WatchlistPause] = "⏸ На паузу",
        [TextKey.WatchlistResume] = "▶️ Продолжить",
        [TextKey.WatchlistDelete] = "🗑 Удалить",
        [TextKey.WatchlistDeleteConfirm] =
            "Удалить «{0}» вместе со всеми компаниями? Уже найденные вакансии останутся.",
        [TextKey.WatchlistDeleted] = "Список «{0}» удалён.",
        [TextKey.ConfirmYes] = "✅ Да",
        [TextKey.ConfirmNo] = "✖ Нет",

        [TextKey.FilterTitle] = "Фильтр списка «{0}»",
        [TextKey.FilterEmpty] = "Фильтра пока нет — подходит любая вакансия этих компаний.",
        [TextKey.FilterKeywords] = "🔍 Слова в названии",
        [TextKey.FilterExcluded] = "🚫 Исключить слова",
        [TextKey.FilterLocations] = "📍 Локации",
        [TextKey.FilterLocationsExcluded] = "🚫 Исключить локации",
        [TextKey.FilterDescription] = "📝 Слова в тексте",
        [TextKey.FilterDescriptionExcluded] = "🚫 Исключить в тексте",
        [TextKey.FilterFreshness] = "🗓 Свежесть",
        [TextKey.FilterTitleButton] = "🔍 Название",
        [TextKey.FilterLocationButton] = "📍 Локация",
        [TextKey.FilterTextButton] = "📝 Текст вакансии",
        [TextKey.FilterKeywordsPrompt] =
            "Слова в <b>названии</b> вакансии. Нужные пишите как есть, исключённые — с минусом: "
            + "<b>backend, sre, -intern, -manager</b>. Достаточно совпадения с любым нужным словом, а любое "
            + "исключённое отбрасывает вакансию.",
        [TextKey.FilterLocationsPrompt] =
            "<b>Локации</b>. Подходящие — как есть, неподходящие — с минусом: <b>remote, берлин, -usa, -индия</b>. "
            + "Вакансия с исключённой локацией отбрасывается, что бы ни говорили остальные правила.",
        [TextKey.FilterDescriptionPrompt] =
            "Слова в <b>тексте</b> вакансии. Нужные — как есть, исключённые — с минусом: "
            + "<b>kubernetes, postgres, -on-site, -security clearance</b>.<br>"
            + "Текст читается при опросе компании: найденные раньше вакансии по этому правилу не перепроверяются, "
            + "а вакансия с нечитаемым текстом не подходит под нужные слова и проходит мимо исключённых.",
        [TextKey.FilterFreshnessPrompt] = "Насколько старой может быть вакансия?",
        [TextKey.FilterFreshnessAny] = "Любая",
        [TextKey.FilterClear] = "🧹 Очистить фильтр",
        [TextKey.FilterCurrent] =
            "Сейчас (нажмите, чтобы скопировать): <code>{0}</code>",
        [TextKey.FilterCurrentEmpty] = "Сейчас правило пустое.",
        [TextKey.FilterEditModes] =
            "Слова <b>добавляются</b> к текущим; слово, которое уже было в другом списке, переносится. "
            + "Начните ответ с <b>=</b>, чтобы заменить поле целиком — скопируйте текущее значение ниже, "
            + "поправьте и отправьте. Одиночный <b>-</b> очищает поле.",
        [TextKey.FilterUnchanged] = "Правило не изменилось — эти слова в нём уже есть.",
        [TextKey.FilterSaved] = "Фильтр обновлён. Сохранённые вакансии перепроверю на следующем круге.",
        [TextKey.FilterCleared] = "Фильтр очищен — теперь подходит любая вакансия этих компаний.",
        [TextKey.FilterAnyValue] = "любые",
        [TextKey.FilterDays] = "последние {0} дней",

        [TextKey.CompaniesTitle] = "Компании списка «{0}»",
        [TextKey.CompaniesEmpty] = "Здесь пока нет компаний. Добавьте первую.",
        [TextKey.CompanyStatusActive] = "следим",
        [TextKey.CompanyStatusDisabled] = "отключена",
        [TextKey.CompanyStatusWorked] = "проработана",
        [TextKey.CompaniesDisabledCount] = "⏸ Отключено компаний: <b>{0}</b> — они на экране отключённых.",
        [TextKey.CompaniesAllDisabled] = "Все компании этого списка отключены.",
        [TextKey.CompanyLegend] = "▶️ следим · ✅ проработана",
        [TextKey.CompanyMarkWorked] = "✅ Отметить проработанной",
        [TextKey.CompanyUnmarkWorked] = "↩️ Снять отметку",
        [TextKey.CompanyDisable] = "⏸ Отключить",
        [TextKey.CompanyEnable] = "▶️ Включить",
        [TextKey.CompanyRemove] = "🗑 Удалить",
        [TextKey.CompanyMarkedWorked] = "«{0}» отмечена как проработанная.",
        [TextKey.CompanyUnmarkedWorked] = "С «{0}» снята отметка «проработана».",
        [TextKey.CompanyDisabled] = "«{0}» отключена — больше за ней не слежу.",
        [TextKey.CompanyEnabled] = "«{0}» снова активна.",
        [TextKey.CompanyRemoved] = "«{0}» удалена.",
        [TextKey.CompanyDisabledInsteadOfRemoved] =
            "«{0}» была найдена автоматически, поэтому она отключена, а не удалена — иначе вернулась бы на "
            + "следующем проходе.",
        [TextKey.CompanyWorkedOn] = "резюме отправлено {0}",
        [TextKey.CompanyFoundByDiscovery] = "найдена автоматически",
        [TextKey.CompanyChange] = "🔧 Изменить компанию",
        [TextKey.CompanyFindPrompt] =
            "Пришлите название компании, которую нужно изменить. Достаточно части названия.",
        [TextKey.CompanyFindNotFound] = "В этом списке нет компании «{0}». Попробуйте другое название.",
        [TextKey.CompanyFindMany] = "Под «{0}» подходит несколько компаний — выберите одну.",
        [TextKey.CompanyCounts] = "{0}",
        [TextKey.CompanyCountsLegend] =
            "После каждой компании: сколько вакансий найдено на её сайте, подходящих под фильтр",
        // Short on purpose: the four groupings share one keyboard row.
        [TextKey.CompaniesBySource] = "🏢 По источнику",
        [TextKey.CompaniesByLocation] = "📍 По локации",
        [TextKey.CompaniesByMonth] = "🗓 По месяцам",
        [TextKey.CompaniesByActivity] = "🔥 По активности",

        [TextKey.DisabledTitle] = "Отключённые компании",
        [TextKey.DisabledEmpty] = "Отключённых нет — слежу за всеми вашими компаниями.",
        [TextKey.DisabledHint] = "Нажмите на компанию, чтобы снова начать за ней следить.",

        [TextKey.AddCompanyPrompt] =
            "Пришлите название компании, например <b>Nebius</b> — или ссылку на её страницу вакансий.",
        [TextKey.AddCompanySearching] = "Ищу «{0}»…",
        [TextKey.AddCompanyNotFound] =
            "Не нашёл «{0}». Попробуйте точное название или пришлите ссылку на страницу вакансий.",
        [TextKey.AddCompanyAlready] = "«{0}» уже есть в этом списке.",
        [TextKey.AddCompanyAdded] = "«{0}» добавлена. Теперь буду сообщать об изменениях.",
        [TextKey.AddCompanyChoose] = "Какую из них вы имеете в виду?",
        [TextKey.AddCompanyVacancies] = "вакансий: {0}",

        [TextKey.VacanciesTitle] = "Вакансии списка «{0}»",
        [TextKey.VacanciesPickWatchlist] = "Выберите список, чтобы увидеть найденные для него вакансии.",
        [TextKey.VacanciesEmpty] =
            "Пока ничего не найдено. Либо у компаний нет подходящих вакансий, либо первый круг ещё идёт.",
        [TextKey.VacanciesCount] = "Подходящих открытых вакансий: {0}.",
        [TextKey.VacanciesShownOf] = "Показаны {0} вакансий из {1} — самые свежие.",
        [TextKey.VacancyUnknownLocation] = "Локация неизвестна",
        [TextKey.VacanciesByCompany] = "🏢 По компаниям",
        [TextKey.VacanciesByLocation] = "📍 По локации",
        [TextKey.VacanciesByMonth] = "🗓 По месяцам",
        [TextKey.VacanciesByActivity] = "🔥 По активности",

        [TextKey.MonthUnknown] = "Дата неизвестна",

        [TextKey.ActivityBlazing] = "Кипит",
        [TextKey.ActivityHot] = "Оживлённо",
        [TextKey.ActivityWarm] = "Вяло",
        [TextKey.ActivityStill] = "Ничего не менялось",
        [TextKey.ActivityRate] = "{0}/мес",
        [TextKey.ActivityBreakdown] = "+{0} / ✏️{1} / ❌{2}",
        [TextKey.ActivityLegend] =
            "Активность: сколько событий с вакансиями в месяц на сайте компании — появилось / изменилось / закрылось. "
            + "Неправдоподобное число обычно значит, что сайт переписывает объявления, а не что там нанимают.",

        [TextKey.RegionEurope] = "Европа",
        [TextKey.RegionRemote] = "Удалённо",
        [TextKey.RegionCis] = "СНГ",
        [TextKey.RegionAmericas] = "Америка",
        [TextKey.RegionAsia] = "Азия",
        [TextKey.RegionMiddleEastAndAfrica] = "Ближний Восток и Африка",
        [TextKey.RegionOceania] = "Австралия и Океания",
        [TextKey.RegionUnknown] = "Локация непонятна",

        [TextKey.StatsTitle] = "📊 {0} · за {1}",
        [TextKey.StatsDigestTitle] = "📊 Сводка за {1} · {0}",
        [TextKey.StatsDigestSinceTitle] = "📊 Что изменилось с прошлой сводки · {0}",
        [TextKey.StatsOpenVacancies] = "📦 Открытых вакансий: <b>{0}</b> (было {1}, {2})",
        [TextKey.StatsOpenCompanies] = "🏢 Компаний с вакансиями: <b>{0}</b> (было {1}, {2})",
        [TextKey.StatsPeriod] = "{0} – {1} UTC",
        [TextKey.StatsDaysOne] = "{0} день",
        [TextKey.StatsDaysFew] = "{0} дня",
        [TextKey.StatsDaysMany] = "{0} дней",
        [TextKey.StatsOpened] = "🆕 Открылось вакансий: <b>{0}</b>",
        [TextKey.StatsClosed] = "❌ Закрылось вакансий: <b>{0}</b>",
        [TextKey.StatsNewCompanies] = "🏢 Новых компаний с вакансиями: <b>{0}</b>",
        [TextKey.StatsEmptiedCompanies] = "🏁 Компаний, закрывших все подходящие вакансии: <b>{0}</b>",
        [TextKey.StatsTopActivity] = "🔥 Самые активные компании",
        [TextKey.StatsTopOpened] = "📈 Больше всего новых вакансий",
        [TextKey.StatsActivityRow] = "событий: {0} ({1})",
        [TextKey.StatsOpenedRow] = "новых: {0}",
        [TextKey.StatsNothing] = "за период ничего",
        [TextKey.StatsDaysButton] = "{0} дн.",
        [TextKey.StatsCustom] = "✏️ Другой период",
        [TextKey.StatsCustomPrompt] = "За сколько дней посчитать статистику? Пришлите число от 1 до {0}.",
        [TextKey.StatsCustomInvalid] = "Это не число дней от 1 до {0}. Пришлите другое.",

        [TextKey.AreaWesternEurope] = "🇪🇺 Западная Европа",
        [TextKey.AreaEasternEurope] = "🇪🇺 Восточная Европа",
        [TextKey.AreaUsa] = "🇺🇸 США",
        [TextKey.AreaAsia] = "🌏 Азия",
        [TextKey.ShortlistTitle] = "🎯 Выжимка · {0}",
        [TextKey.ShortlistFresh] = "Вакансии, опубликованные за последние {0}.",
        [TextKey.ShortlistRegion] = "Вакансии в регионе: {0}.",
        [TextKey.ShortlistHint] = "Топ-{0} компаний по числу подходящих вакансий (отключённые не учитываются), в каждой {1} лучших для отклика: больше всего слов из фильтра заголовка, затем самые свежие. Название компании открывает все её вакансии.",
        [TextKey.ShortlistCompanyRow] = "подходящих: {0}",
        [TextKey.ShortlistWorked] = "✅ отработана",
        [TextKey.ShortlistEmpty] = "В этом срезе ничего не нашлось.",
        [TextKey.ShortlistCustomPrompt] = "Вакансии за сколько последних дней? Пришлите число от 1 до {0}.",
        [TextKey.CompanyVacanciesTitle] = "🏢 {0} · {1}",
        [TextKey.CompanyVacanciesEmpty] = "Подходящих открытых вакансий у этой компании сейчас нет.",
        [TextKey.CompanyVacanciesAll] = "📋 Все вакансии списка",

        [TextKey.RunTitlePolling] = "🔄 Прогон polling · {0}",
        [TextKey.RunTitleRegistry] = "🗂 Прогон реестра · {0}",
        [TextKey.RunPeriod] = "⏱ Прогон: {0}",
        [TextKey.ChangesPeriod] = "🗓 Изменения: {0}",
        [TextKey.ChangesFirstPoll] = "🗓 Изменения: все компании опрошены впервые",
        [TextKey.DiscoveryCollections] = "🗓 Индексы краулинга: {0}",
        [TextKey.RunWalked] = "Обойдено компаний: <b>{0}</b>, с ошибкой: {1}, изменений: {2}",
        [TextKey.RunUnfinished] = "Прогон остановился раньше времени — ниже то, что он успел сохранить.",
        [TextKey.DiscoveryTitle] = "🔎 Прогон discovery",
        [TextKey.DiscoveryTitleFull] = "🔎 Прогон discovery (полный)",
        [TextKey.DiscoveryCounts] =
            "Индексов краулинга: <b>{0}</b> (с ошибкой {1}, осталось {2})<br>Прочитано записей: {3}<br>"
            + "Токенов досок: {4}, проверено: {5}<br>Новых досок в реестре: <b>{6}</b>",

        [TextKey.LanguageTitle] = "Выберите язык",
        [TextKey.LanguageChanged] = "Язык переключён на русский.",

        [TextKey.DigestAllChanges] = "📋 Все изменения",
        [TextKey.DigestChangesTitle] = "📋 Изменения · {0}",
        [TextKey.DigestChangesEmpty] = "За этот период ничего не изменилось.",
        [TextKey.DigestNewCompanies] = "🏢 Новые компании: <b>{0}</b>",
        [TextKey.DigestEmptiedCompanies] = "🏁 Компании, оставшиеся без вакансий: <b>{0}</b>",
        [TextKey.DigestCompanyRow] = "🆕 {0} · ушло {1}",
        [TextKey.DigestUnknownTitle] = "вакансия {0}",

        [TextKey.SilentModeOn] = "Тихий режим включён: ни отчётов после прогонов, ни уведомлений о вакансиях. Приходит только дайджест.",
        [TextKey.SilentModeOff] = "Тихий режим выключен: вакансии и отчёт после каждого прогона.",

        [TextKey.Help] =
            "<b>Как это работает</b><br>"
            + "1. Создайте список наблюдения — набор компаний с названием.<br>"
            + "2. Добавьте компании по названию или по ссылке на страницу вакансий.<br>"
            + "3. Настройте фильтр, чтобы приходили только нужные вакансии.<br>"
            + "4. Я регулярно проверяю компании и присылаю новые, изменённые и закрытые вакансии.<br><br>"
            + "Отмечайте компанию как ✅ проработанную после отправки резюме — в списке она будет выделяться. "
            + "Неинтересные сейчас компании можно отключить и вернуть позже.",
        [TextKey.UnknownCommand] = "Не понял. Вот меню.",
        [TextKey.SessionExpired] = "Этот шаг устарел — начните заново из меню.",
        [TextKey.NotAllowed] = "Это изменить нельзя.",
        [TextKey.AdminOnly] = "Этот раздел только для администраторов.",
        [TextKey.SomethingWentWrong] = "Что-то пошло не так. Попробуйте ещё раз через минуту.",
        [TextKey.Saved] = "Сохранено.",
        [TextKey.Nothing] = "—",

        [TextKey.NotificationNew] = "Новая",
        [TextKey.NotificationUpdated] = "Изменилась",
        [TextKey.NotificationClosed] = "Закрыта",
        [TextKey.NotificationNewBoard] = "Найдена новая компания",
        [TextKey.NotificationWindow] = "🕔 {0}, {1}–{2} UTC",
        [TextKey.NotificationWindowCounts] = "Изменений: {0}, компаний: {1}"
    };
}
