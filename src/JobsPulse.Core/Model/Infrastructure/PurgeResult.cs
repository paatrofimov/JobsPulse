namespace JobsPulse.Core.Model.Infrastructure;

public readonly record struct PurgeResult(
    int SeenVacanciesDeleted,
    int OutboxDeleted,
    int BoardsDeleted,
    int CrawlIndexStateDeleted,
    int WatchlistMatchesDeleted);
