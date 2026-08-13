namespace JobsPulse.Sinks.Telegram.Infrastructure.Localization;

/// <summary>Russian side of the text table. Must hold every <see cref="TextKey"/> - see <see cref="BotTexts"/>.</summary>
internal static class RussianTexts
{
    internal static readonly string[] Months =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
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
        [TextKey.FilterFreshness] = "🗓 Свежесть",
        [TextKey.FilterKeywordsPrompt] =
            "Пришлите слова, которые должны быть в названии вакансии, через запятую — например "
            + "<b>backend, sre, платформа</b>. Достаточно совпадения с любым из них. Пришлите <b>-</b>, чтобы очистить.",
        [TextKey.FilterExcludedPrompt] =
            "Пришлите слова, которых в названии быть <b>не</b> должно, через запятую — например "
            + "<b>стажёр, продажи</b>. Пришлите <b>-</b>, чтобы очистить.",
        [TextKey.FilterLocationsPrompt] =
            "Пришлите подходящие локации через запятую — например <b>remote, берлин, польша</b>. "
            + "Пришлите <b>-</b>, чтобы очистить.",
        [TextKey.FilterFreshnessPrompt] = "Насколько старой может быть вакансия?",
        [TextKey.FilterFreshnessAny] = "Любая",
        [TextKey.FilterClear] = "🧹 Очистить фильтр",
        [TextKey.FilterSaved] = "Фильтр обновлён. Сохранённые вакансии перепроверю на следующем круге.",
        [TextKey.FilterCleared] = "Фильтр очищен — теперь подходит любая вакансия этих компаний.",
        [TextKey.FilterAnyValue] = "любые",
        [TextKey.FilterDays] = "последние {0} дней",

        [TextKey.CompaniesTitle] = "Компании списка «{0}»",
        [TextKey.CompaniesEmpty] = "Здесь пока нет компаний. Добавьте первую.",
        [TextKey.CompanyStatusActive] = "следим",
        [TextKey.CompanyStatusDisabled] = "отключена",
        [TextKey.CompanyStatusWorked] = "проработана",
        [TextKey.CompanyLegend] = "▶️ следим · ✅ проработана · ⏸ отключена",
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

        [TextKey.LanguageTitle] = "Выберите язык",
        [TextKey.LanguageChanged] = "Язык переключён на русский.",

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
        [TextKey.NotificationNewBoard] = "Найдена новая компания"
    };
}
