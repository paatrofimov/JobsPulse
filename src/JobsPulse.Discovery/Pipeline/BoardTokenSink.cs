using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Discovery.Infrastructure;
using JobsPulse.Discovery.Options;
using Microsoft.Extensions.Options;
using Vostok.Logging.Abstractions;

namespace JobsPulse.Discovery.Pipeline;

/// <summary>
/// The last stage of every discovery pass, whichever index it read: a board token exists only if the ATS itself
/// answers for it, so unknown tokens are probed and only the survivors reach the registry.
/// </summary>
public sealed class BoardTokenSink(
    ISourceCatalog sources,
    IBoardRegistryStorage registry,
    TimeProvider clock,
    ILog log)
{
    private readonly ILog ctxLog = log.ForContext<BoardTokenSink>();

    /// <summary>Returns the number of newly inserted registry rows.</summary>
    public async Task<int> ValidateAndStoreAsync(
        string sourceId,
        IReadOnlyList<string> tokens,
        string discoveredVia,
        DiscoveryOptions opts,
        CancellationToken ct)
    {
        if (tokens.Count == 0)
            return 0;

        var resolver = sources.GetResolver(sourceId);
        if (resolver is null)
        {
            ctxLog.Warn("Source '{Source}' has no resolver — tokens cannot be validated", sourceId);
            return 0;
        }

        using var stage = StageTimer.Start(ctxLog, $"validation of {tokens.Count} {sourceId} tokens from {discoveredVia}");

        var now = clock.GetUtcNow();

        using var gate = new SemaphoreSlim(Math.Max(1, opts.ValidationConcurrency));

        var added = 0;
        var probed = 0;
        var batchSize = Math.Max(1, opts.UpsertBatchSize);
        var batch = new List<RegisteredBoard>(batchSize);

        foreach (var chunk in tokens.Chunk(batchSize))
        {
            var probes = await Task.WhenAll(chunk.Select(async token =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    return await ProbeSafeAsync(resolver, token, ct);
                }
                finally
                {
                    gate.Release();
                }
            }));

            batch.Clear();
            batch.AddRange(probes
                .Where(c => c is not null)
                .Select(c => new RegisteredBoard
                {
                    SourceId = c!.SourceId,
                    // The probe decides the board id: for an ATS whose token carries a guess (Workday's tenant) the
                    // confirmed address is what must be stored, not what the index suggested.
                    BoardId = c.BoardId,
                    DisplayName = c.DisplayName,
                    Configuration = c.Configuration,
                    JobCount = c.JobCount,
                    BoardUrl = c.BoardUrl,
                    DiscoveredVia = discoveredVia,
                    DiscoveredAt = now,
                    LastValidatedAt = now,
                    IsActive = true
                }));

            if (batch.Count > 0)
                added += await registry.UpsertAsync(batch, ct);

            probed += chunk.Length;

            ctxLog.Debug(
                "Validated {Probed}/{Total} {Source} tokens, {Added} new boards so far ({Elapsed})",
                probed, tokens.Count, sourceId, added, stage.Elapsed);
        }

        ctxLog.Info(
            "Validation of {Via}: {Tokens} {Source} tokens, {Added} new boards",
            discoveredVia, tokens.Count, sourceId, added);

        return added;
    }

    private async Task<BoardCandidate?> ProbeSafeAsync(IBoardResolver resolver, string token, CancellationToken ct)
    {
        try
        {
            return await resolver.ProbeAsync(token, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ctxLog.Debug(ex, "Validation of board token '{Token}' has failed", token);
            return null;
        }
    }
}
