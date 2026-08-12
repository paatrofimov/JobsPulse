using JobsPulse.Core.Pipeline;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure.Localization;
using JobsPulse.Sinks.Telegram.Models;

namespace JobsPulse.Sinks.Telegram.Pipeline.Screens;

/// <summary>
/// Adding a company by what the user actually knows - its name or a link to its careers page. The ATS, the board id
/// and the source are never asked for; that is what <see cref="WatchService.LookupAsync"/> is for, and the candidates
/// it returns become buttons.
/// </summary>
public sealed class AddCompanyScreen(
    WatchService watch,
    WatchlistAccess access,
    UserSessionStore sessions,
    CompaniesScreen companies)
{
    public async Task<ScreenView> PromptAsync(BotContext ctx, long watchlistId, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is null)
            return Gone(ctx);

        if (!resolved.CanEdit)
        {
            return new ScreenView(
                $"<p>{BotTexts.Get(TextKey.WatchlistReadOnly, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.WatchlistOpen, watchlistId));
        }

        sessions.Await(ctx.UserId, PendingInputKind.CompanyQuery, watchlistId);

        return new ScreenView(
            $"<p>{BotTexts.Get(TextKey.AddCompanyPrompt, ctx.Language)}</p>",
            new KeyboardBuilder(ctx.Language).Build(CallbackAction.WatchlistOpen, watchlistId));
    }

    public async Task<ScreenView> SearchAsync(BotContext ctx, long watchlistId, string query, CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        if (!resolved.CanEdit)
        {
            return new ScreenView(
                $"<p>{BotTexts.Get(TextKey.WatchlistReadOnly, ctx.Language)}</p>",
                new KeyboardBuilder(ctx.Language).Build(CallbackAction.WatchlistOpen, watchlistId));
        }

        var result = await watch.LookupAsync(watchlist, query, ct);

        switch (result.Status)
        {
            case LookupStatus.AlreadyWatched:
                return (await companies.RenderAsync(ctx, watchlistId, 0, ct))
                    .WithToast(BotTexts.Get(
                        TextKey.AddCompanyAlready, ctx.Language, result.Existing!.CompanyName));

            case LookupStatus.NotFound:
                // The step stays armed: a miss is usually a typo, and retyping beats walking the menu again.
                sessions.Await(ctx.UserId, PendingInputKind.CompanyQuery, watchlistId);

                return new ScreenView(
                    $"<p>{BotTexts.Get(TextKey.AddCompanyNotFound, ctx.Language, MessageFormatter.Escape(query))}</p>",
                    new KeyboardBuilder(ctx.Language).Build(CallbackAction.WatchlistOpen, watchlistId));

            case LookupStatus.Found when result.Candidates.Count == 1:
                return await AddAsync(ctx, watchlist.Id, result.Candidates[0], ct);

            default:
                sessions.SetCandidates(ctx.UserId, watchlistId, result.Candidates);

                var builder = new KeyboardBuilder(ctx.Language);

                for (var i = 0; i < result.Candidates.Count; i++)
                {
                    var candidate = result.Candidates[i];

                    builder.Row(KeyboardBuilder.Make(
                        $"{candidate.DisplayName} — "
                        + BotTexts.Get(TextKey.AddCompanyVacancies, ctx.Language, candidate.JobCount),
                        CallbackAction.CompanyPick,
                        i));
                }

                return new ScreenView(
                    $"<p>{BotTexts.Get(TextKey.AddCompanyChoose, ctx.Language)}</p>",
                    builder.Build(CallbackAction.WatchlistOpen, watchlistId));
        }
    }

    /// <summary>The button carries the index into the candidate list held in the session, not a board id.</summary>
    public async Task<ScreenView> PickAsync(BotContext ctx, long index, CancellationToken ct)
    {
        var session = sessions.Take(ctx.UserId);
        if (session is null || session.Candidates.Count == 0)
            return Expired(ctx);

        if (index < 0 || index >= session.Candidates.Count)
            return Expired(ctx);

        var resolved = await access.ResolveAsync(ctx, session.WatchlistId, ct);
        if (resolved.Watchlist is null)
            return Gone(ctx);

        if (!resolved.CanEdit)
        {
            return (await companies.RenderAsync(ctx, session.WatchlistId, 0, ct))
                .WithToast(BotTexts.Get(TextKey.NotAllowed, ctx.Language));
        }

        return await AddAsync(ctx, session.WatchlistId, session.Candidates[(int)index], ct);
    }

    private async Task<ScreenView> AddAsync(
        BotContext ctx,
        long watchlistId,
        Core.Model.Infrastructure.BoardCandidate candidate,
        CancellationToken ct)
    {
        var resolved = await access.ResolveAsync(ctx, watchlistId, ct);
        if (resolved.Watchlist is not { } watchlist)
            return Gone(ctx);

        var entry = await watch.AddCandidateAsync(watchlist, candidate, ct);
        if (entry is null)
            return Gone(ctx);

        sessions.Clear(ctx.UserId);

        return (await companies.RenderAsync(ctx, watchlistId, 0, ct))
            .WithToast(BotTexts.Get(TextKey.AddCompanyAdded, ctx.Language, entry.CompanyName));
    }

    private static ScreenView Expired(BotContext ctx) =>
        new(
            $"<p>{BotTexts.Get(TextKey.SessionExpired, ctx.Language)}</p>",
            new KeyboardBuilder(ctx.Language).Build(CallbackAction.MyWatchlists));

    private static ScreenView Gone(BotContext ctx) =>
        new(
            $"<p>{BotTexts.Get(TextKey.WatchlistGone, ctx.Language)}</p>",
            new KeyboardBuilder(ctx.Language).Build(CallbackAction.MyWatchlists));
}
