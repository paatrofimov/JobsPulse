using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Model;
using JobsPulse.Core.Pipeline;
using Xunit;

namespace JobsPulse.Core.Tests;

/// <summary>
/// Самые дорогие ошибки системы — ложные уведомления. Здесь они и проверяются.
/// </summary>
public sealed class ChangeDetectorTests
{
    private readonly ChangeDetector _detector = new();

    [Fact]
    public void Unknown_vacancy_is_new()
    {
        var vacancy = TestData.Vacancy();

        var result = Detect([vacancy], seen: [], complete: true);

        Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.New, result.Changes[0].Kind);
    }

    [Fact]
    public void Unchanged_vacancy_produces_no_notification()
    {
        var vacancy = TestData.Vacancy();

        var result = Detect([vacancy], seen: [TestData.Seen(vacancy)], complete: true);

        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Same_updated_at_but_changed_title_is_update()
    {
        // Обратная сторона: сравниваем контент, а не updated_at.
        var before = TestData.Vacancy(title: "Backend Engineer");
        var after = before with { Title = "Senior Backend Engineer" };

        var result = Detect([after], seen: [TestData.Seen(before)], complete: true);

        Assert.Single(result.Changes);
        Assert.Equal(ChangeKind.Updated, result.Changes[0].Kind);
    }

    [Fact]
    public void Incomplete_fetch_never_closes_anything()
    {
        // Главный предохранитель: сетевая ошибка не должна «закрыть» весь борд.
        var vacancy = TestData.Vacancy();

        var result = Detect([], seen: [TestData.Seen(vacancy)], complete: false);

        Assert.Empty(result.Closed);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void Complete_fetch_closes_missing_vacancy()
    {
        var vacancy = TestData.Vacancy();

        var result = Detect([], seen: [TestData.Seen(vacancy)], complete: true);

        Assert.Single(result.Closed);
        Assert.Equal(ChangeKind.Closed, result.Changes[0].Kind);
        Assert.Equal(vacancy.Title, result.Changes[0].Vacancy.Title);
    }

    [Fact]
    public void Duplicate_posts_of_one_job_collapse()
    {
        // Greenhouse публикует одну вакансию несколькими постами — в чат должен уйти один.
        var first = TestData.Vacancy(externalId: "1", groupId: "100");
        var second = TestData.Vacancy(externalId: "2", groupId: "100");

        var result = Detect([first, second], seen: [], complete: true);

        Assert.Single(result.Upserts);
        Assert.Single(result.Changes);
    }

    [Fact]
    public void Posts_without_group_id_are_not_collapsed()
    {
        var first = TestData.Vacancy(externalId: "1", groupId: null);
        var second = TestData.Vacancy(externalId: "2", groupId: null);

        var result = Detect([first, second], seen: [], complete: true);

        Assert.Equal(2, result.Upserts.Count);
    }

    private ChangeDetector.Output Detect(
        IReadOnlyList<Vacancy> matched, IReadOnlyList<SeenVacancy> seen, bool complete) =>
        _detector.Detect(new ChangeDetector.Input
        {
            Entry = TestData.Entry(),
            Fetch = complete
                ? SourceFetchResult.Complete(matched)
                : SourceFetchResult.Failed("timeout"),
            Matched = matched,
            Seen = seen.ToDictionary(s => s.ExternalId, StringComparer.Ordinal)
        });
}
