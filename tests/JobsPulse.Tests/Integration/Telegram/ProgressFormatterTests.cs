using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sinks.Telegram.Models;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

/// <summary>
/// The admin progress block. It is rendered from state that is often empty - a fresh installation, a job that has
/// never run - so «renders without dividing by zero» is as much the point as the wording itself.
/// </summary>
public sealed class ProgressFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Render_should_say_when_polling_ran_and_how_much_it_walked()
    {
        var stamps = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase)
        {
            ["greenhouse/a"] = Now.AddHours(-2),
            ["greenhouse/b"] = Now.AddHours(-2),
            ["lever/c"] = Now.AddHours(-9)
        };

        var snapshot = new ProgressSnapshot
        {
            Now = Now,
            Runs =
            [
                Run(JobRunRoles.Polling, Now.AddHours(-2), TimeSpan.FromMinutes(21), JobRunOutcome.Succeeded,
                    new JobRunSummary { BoardsProcessed = 3, BoardsFailed = 1, VacanciesFetched = 1200, Changes = 5 })
            ],
            Watchlist = BoardCoverage.Compute(
                [("greenhouse", "greenhouse/a"), ("greenhouse", "greenhouse/b"), ("lever", "lever/c"), ("lever", "lever/d")],
                stamps,
                Now.AddHours(-2))
        };

        var html = ProgressFormatter.Render(snapshot, BotLanguage.Russian);

        html.Should().Contain("Последний запуск: 30.09 10:00 (2 ч 0 мин назад), длился 21 мин — ✅ успешно");
        html.Should().Contain("обошли <b>3</b> доски, с ошибкой 1");
        html.Should().Contain("Досок в watchlist'ах: <b>4</b>");
        html.Should().Contain("опрошено <b>2</b> (50%)");
        html.Should().Contain("ни разу не опрашивались: <b>1</b>");
        html.Should().Contain("самый давний опрос — 9 ч 0 мин назад");
    }

    [Test]
    public void Render_should_name_the_last_successful_run_after_a_failure()
    {
        var snapshot = new ProgressSnapshot
        {
            Now = Now,
            Runs =
            [
                Run(JobRunRoles.Polling, Now.AddHours(-3), TimeSpan.FromMinutes(18), JobRunOutcome.Failed,
                    JobRunSummary.None, "HttpIOException: The response ended prematurely."),
                Run(JobRunRoles.Polling, Now.AddHours(-6), TimeSpan.FromMinutes(18), JobRunOutcome.Succeeded,
                    JobRunSummary.None)
            ]
        };

        var html = ProgressFormatter.Render(snapshot, BotLanguage.English);

        html.Should().Contain("❌ failed: <code>HttpIOException: The response ended prematurely.</code>");
        html.Should().Contain("Last successful: 30.09 06:00 (6h 0m ago)");
    }

    [Test]
    public void Render_should_tell_a_running_job_from_a_killed_one()
    {
        var snapshot = new ProgressSnapshot
        {
            Now = Now,
            Runs =
            [
                Run(JobRunRoles.Registry, Now.AddMinutes(-20), null, JobRunOutcome.Running, JobRunSummary.None),
                Run(JobRunRoles.Discovery, Now.AddHours(-10), null, JobRunOutcome.Running, JobRunSummary.None)
            ]
        };

        var html = ProgressFormatter.Render(snapshot, BotLanguage.English);

        html.Should().Contain("⏳ running now, for 20m");
        html.Should().Contain("⚠️ never finished");
    }

    [Test]
    public void Render_should_report_registry_coverage_per_source_and_the_discovery_totals()
    {
        var stamps = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase)
        {
            ["greenhouse/a"] = Now.AddHours(-1),
            ["greenhouse/b"] = Now.AddDays(-3)
        };

        var snapshot = new ProgressSnapshot
        {
            Now = Now,
            Registry = BoardCoverage.Compute(
                [("greenhouse", "greenhouse/a"), ("greenhouse", "greenhouse/b")], stamps, Now.AddDays(-1)),
            RegistryInactive = 7,
            KnownBySource = new Dictionary<string, int> { ["greenhouse"] = 6014, ["lever"] = 1744 },
            Runs =
            [
                Run(JobRunRoles.Discovery, Now.AddHours(-11), TimeSpan.FromMinutes(10), JobRunOutcome.Succeeded,
                    new JobRunSummary
                    {
                        CollectionsProcessed = 0, CollectionsFailed = 1, CollectionsPending = 2, RecordsSeen = 23437,
                        TokensFound = 17, BoardsAdded = 0
                    })
            ]
        };

        var html = ProgressFormatter.Render(snapshot, BotLanguage.English);

        html.Should().Contain("Active boards outside the watchlists: <b>2</b> (7 more switched off");
        html.Should().Contain("Polled within the last 24 h: <b>1</b> (50%)");
        html.Should().Contain("<code>greenhouse</code>: 1 of 2 (50%)");
        html.Should().Contain("1 failed, 2 left for the next run");
        html.Should().Contain("Boards known in total: <b>7,758</b> - greenhouse 6,014, lever 1,744");
    }

    [Test]
    public void Render_should_survive_an_installation_where_nothing_has_run()
    {
        var html = ProgressFormatter.Render(new ProgressSnapshot { Now = Now }, BotLanguage.Russian);

        html.Should().Contain("Запусков за последние 30 дней не было");
        html.Should().Contain("Досок ещё не найдено");
        html.Should().NotContain("%");
    }

    private static JobRun Run(
        string role,
        DateTimeOffset started,
        TimeSpan? took,
        JobRunOutcome outcome,
        JobRunSummary summary,
        string? error = null) => new()
    {
        Role = role,
        StartedAt = started,
        FinishedAt = took is { } span ? started + span : null,
        Outcome = outcome,
        Error = error,
        Summary = summary
    };
}
