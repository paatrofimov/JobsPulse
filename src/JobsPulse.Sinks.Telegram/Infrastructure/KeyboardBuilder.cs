using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace JobsPulse.Sinks.Telegram.Infrastructure;

/// <summary>
/// Builds inline keyboards. Every screen ends with a navigation row, so there is always a way back - a user who
/// taps into a dead end and has to retype /start is a bug, not a preference.
/// </summary>
public sealed class KeyboardBuilder(BotLanguage language)
{
    /// <summary>Telegram renders more than this per screen unreadably, and paging is nicer than scrolling.</summary>
    public const int PageSize = 8;

    private readonly List<InlineKeyboardButton[]> rows = [];

    public KeyboardBuilder Row(params InlineKeyboardButton[] buttons)
    {
        if (buttons.Length > 0)
            rows.Add(buttons);

        return this;
    }

    public KeyboardBuilder Button(TextKey label, CallbackAction action, long id = 0, int page = 0) =>
        Row(Make(BotTexts.Get(label, language), action, id, page));

    public KeyboardBuilder ButtonIf(bool condition, TextKey label, CallbackAction action, long id = 0, int page = 0) =>
        condition ? Button(label, action, id, page) : this;

    /// <summary>
    /// Two buttons on one row - for the «wanted / unwanted» pairs of the filter, where the two halves of one rule
    /// belong together and a row each would only make the screen longer.
    /// </summary>
    public KeyboardBuilder Pair(
        (TextKey Label, CallbackAction Action) left,
        (TextKey Label, CallbackAction Action) right,
        long id = 0,
        int page = 0) =>
        Row(
            Make(BotTexts.Get(left.Label, language), left.Action, id, page),
            Make(BotTexts.Get(right.Label, language), right.Action, id, page));

    /// <summary>One button per item, one per row - company and watchlist names are too long to share a row.</summary>
    public KeyboardBuilder Items<T>(
        IEnumerable<T> items,
        Func<T, string> label,
        CallbackAction action,
        Func<T, long> id,
        int page = 0)
    {
        foreach (var item in items)
            Row(Make(label(item), action, id(item), page));

        return this;
    }

    /// <summary>Prev/next row, rendered only when there is more than one page.</summary>
    public KeyboardBuilder Paging(CallbackAction action, long id, int page, int totalPages)
    {
        if (totalPages <= 1)
            return this;

        var buttons = new List<InlineKeyboardButton>();

        if (page > 0)
            buttons.Add(Make(BotTexts.Get(TextKey.PrevPage, language), action, id, page - 1));

        // The label is a button only because a row needs one - it points at the page it is already showing.
        buttons.Add(Make(
            BotTexts.Get(TextKey.Page, language, page + 1, totalPages), action, id, page));

        if (page < totalPages - 1)
            buttons.Add(Make(BotTexts.Get(TextKey.NextPage, language), action, id, page + 1));

        return Row([.. buttons]);
    }

    /// <summary>The closing row. `back` is omitted on the main menu, which is already the root.</summary>
    public InlineKeyboardMarkup Build(CallbackAction back = CallbackAction.None, long backId = 0, int backPage = 0)
    {
        var navigation = new List<InlineKeyboardButton>();

        if (back != CallbackAction.None)
            navigation.Add(Make(BotTexts.Get(TextKey.Back, language), back, backId, backPage));

        navigation.Add(Make(BotTexts.Get(TextKey.ToMenu, language), CallbackAction.Menu));

        rows.Add([.. navigation]);

        return new InlineKeyboardMarkup(rows);
    }

    /// <summary>Keyboard with no navigation row - only for the menu itself, which is the way back.</summary>
    public InlineKeyboardMarkup BuildBare() => new(rows);

    public static InlineKeyboardButton Make(string label, CallbackAction action, long id = 0, int page = 0) =>
        InlineKeyboardButton.WithCallbackData(label, new CallbackData(action, id, page).ToString());

    public static InlineKeyboardButton Link(string label, string url) =>
        InlineKeyboardButton.WithUrl(label, url);
}
