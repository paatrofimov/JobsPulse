using System.Net;
using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Sources.HeadHunter.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.HeadHunter;

public sealed class HeadHunterBoardSourceTests
{
    private static readonly DateTimeOffset Published = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    private static SourceTarget Target(string boardId = "1740") =>
        new() { SourceId = HeadHunterMapper.SourceId, BoardId = boardId };

    [Test]
    public async Task TraverseTarget_should_read_one_page_of_an_employer()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json(
            HeadHunterFixtures.VacancySearch(
                found: 2,
                pages: 1,
                HeadHunterFixtures.Vacancy("1", "Engineer", Published),
                HeadHunterFixtures.Vacancy("2", "Analyst", Published))));

        using var host = new HeadHunterTestHost(api);

        var result = await host.Source.TraverseTargetAsync(Target(), CancellationToken.None);

        result.IsComplete.Should().BeTrue();
        result.Vacancies.Should().HaveCount(2);
        result.Vacancies.Should().OnlyContain(v => v.BoardId == "1740");
        api.QueryOf(0, "employer_id").Should().Be("1740");
    }

    [Test]
    public async Task TraverseTarget_should_page_until_the_last_page_of_the_search()
    {
        var options = HeadHunterTestHost.Fast();
        options.PageSize = 2;

        var api = new HeadHunterStubApi(uri =>
        {
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(uri.Query)["page"]!);

            return HeadHunterStubAnswer.Json(
                HeadHunterFixtures.VacancySearch(
                    found: 4,
                    pages: 2,
                    HeadHunterFixtures.Vacancy($"{page}-1", "Engineer", Published),
                    HeadHunterFixtures.Vacancy($"{page}-2", "Analyst", Published)));
        });

        using var host = new HeadHunterTestHost(api, options);

        var result = await host.Source.TraverseTargetAsync(Target(), CancellationToken.None);

        result.IsComplete.Should().BeTrue();
        result.Vacancies.Should().HaveCount(4);
        api.Requests.Should().HaveCount(2);
    }

    /// <summary>
    /// The search refuses to page past `page` * `per_page`, so an employer bigger than that depth is continued from the
    /// publication date of the oldest vacancy already seen. The boundary is re-read, so the duplicate has to be dropped.
    /// </summary>
    [Test]
    public async Task TraverseTarget_should_continue_in_a_date_window_past_the_paging_depth()
    {
        var options = HeadHunterTestHost.Fast();
        options.PageSize = 2;
        options.MaxPagedItems = 4;

        var api = new HeadHunterStubApi(uri =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var page = int.Parse(query["page"]!);
            var windowed = query["date_to"] is not null;

            if (!windowed)
            {
                // Reports more pages than the depth can address - which is exactly the situation windowing exists for.
                return HeadHunterStubAnswer.Json(
                    HeadHunterFixtures.VacancySearch(
                        found: 6,
                        pages: 3,
                        HeadHunterFixtures.Vacancy($"{page}-1", "Engineer", Published.AddHours(-page * 2)),
                        HeadHunterFixtures.Vacancy($"{page}-2", "Analyst", Published.AddHours(-page * 2 - 1))));
            }

            return HeadHunterStubAnswer.Json(
                HeadHunterFixtures.VacancySearch(
                    found: 2,
                    pages: 1,
                    // The oldest vacancy of the previous window arrives again - `date_to` is inclusive.
                    HeadHunterFixtures.Vacancy("1-2", "Analyst", Published.AddHours(-3)),
                    HeadHunterFixtures.Vacancy("tail", "Designer", Published.AddHours(-4))));
        });

        using var host = new HeadHunterTestHost(api, options);

        var result = await host.Source.TraverseTargetAsync(Target(), CancellationToken.None);

        result.IsComplete.Should().BeTrue();
        result.Vacancies.Select(v => v.PostId).Should().BeEquivalentTo(["0-1", "0-2", "1-1", "1-2", "tail"]);
        api.QueryOf(2, "date_to").Should().NotBeNull();
    }

    /// <summary>
    /// An employer the catalog does not know is HTTP 400 `bad_argument`, not a 404 - and it still has to read as a
    /// missing board, or a deleted employer would be polled forever.
    /// </summary>
    [Test]
    public async Task TraverseTarget_should_report_an_unknown_employer_as_missing()
    {
        var api = new HeadHunterStubApi(_ =>
            HeadHunterStubAnswer.Error(HttpStatusCode.BadRequest, HeadHunterFixtures.UnknownEmployer));

        using var host = new HeadHunterTestHost(api);

        var result = await host.Source.TraverseTargetAsync(Target("999999999"), CancellationToken.None);

        result.BoardMissing.Should().BeTrue();
        result.IsComplete.Should().BeFalse();
    }

    /// <summary>
    /// The two HTTP 400s share nothing but the code: a blacklisted user agent is about the caller, so it must not close
    /// the board the way `bad_argument` does - and it must not be retried either, because no retry can fix a header.
    /// </summary>
    [Test]
    public async Task TraverseTarget_should_not_report_a_blacklisted_user_agent_as_a_missing_board()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Error(
            HttpStatusCode.BadRequest, HeadHunterFixtures.BlacklistedUserAgent));

        var options = HeadHunterTestHost.Fast();
        options.Retries = 3;

        using var host = new HeadHunterTestHost(api, options);

        var result = await host.Source.TraverseTargetAsync(Target(), CancellationToken.None);

        result.BoardMissing.Should().BeFalse();
        result.IsComplete.Should().BeFalse();
        result.Error.Should().NotBeNull();
        api.Requests.Should().HaveCount(1);
    }

    /// <summary>A refused request is the api's verdict on us, never an employer that stopped existing.</summary>
    [Test]
    public async Task TraverseTarget_should_not_report_a_refused_request_as_a_missing_board()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Error(HttpStatusCode.Forbidden));

        using var host = new HeadHunterTestHost(api);

        var result = await host.Source.TraverseTargetAsync(Target(), CancellationToken.None);

        result.BoardMissing.Should().BeFalse();
        result.IsComplete.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    /// <summary>
    /// The board id is the employer id and nothing else, so a row holding anything else is a failure - and one that
    /// costs no request, because a traversal must never fall back to searching for a company.
    /// </summary>
    [Test]
    public async Task TraverseTarget_should_refuse_a_board_id_that_is_not_an_employer_id()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json("{}"));

        using var host = new HeadHunterTestHost(api);

        var result = await host.Source.TraverseTargetAsync(Target("yandex"), CancellationToken.None);

        result.IsComplete.Should().BeFalse();
        result.BoardMissing.Should().BeFalse();
        api.Requests.Should().BeEmpty();
    }

    /// <summary>Partial data must be reported as incomplete: the orchestrator drops it instead of closing what was not read.</summary>
    [Test]
    public async Task TraverseTarget_should_report_the_page_cap_as_incomplete()
    {
        var options = HeadHunterTestHost.Fast();
        options.PageSize = 1;
        options.MaxPages = 1;

        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json(
            HeadHunterFixtures.VacancySearch(
                found: 100,
                pages: 100,
                HeadHunterFixtures.Vacancy("1", "Engineer", Published))));

        using var host = new HeadHunterTestHost(api, options);

        var result = await host.Source.TraverseTargetAsync(Target(), CancellationToken.None);

        result.IsComplete.Should().BeFalse();
        result.Vacancies.Should().HaveCount(1);
        result.Error.Should().Contain("page cap");
    }

    /// <summary>Descriptions are one request per vacancy, so the budget has to be an upper bound on the whole traversal.</summary>
    [Test]
    public async Task TraverseTarget_should_stop_asking_for_descriptions_when_the_budget_is_spent()
    {
        var options = HeadHunterTestHost.Fast();
        options.MaxDescriptionRequests = 1;

        var api = new HeadHunterStubApi(uri => uri.AbsolutePath.StartsWith("/vacancies/")
            ? HeadHunterStubAnswer.Json("""{ "id": "1", "description": "The whole ad" }""")
            : HeadHunterStubAnswer.Json(
                HeadHunterFixtures.VacancySearch(
                    found: 2,
                    pages: 1,
                    HeadHunterFixtures.Vacancy("1", "Engineer", Published),
                    HeadHunterFixtures.Vacancy("2", "Analyst", Published))));

        using var host = new HeadHunterTestHost(api, options);

        var result = await host.Source.TraverseTargetAsync(
            Target() with { IncludeDescriptions = true },
            CancellationToken.None);

        result.IsComplete.Should().BeTrue();
        result.Vacancies[0].Description.Should().Be("The whole ad");
        // The second one keeps the search snippet instead of costing another request.
        result.Vacancies[1].Description.Should().Be("Писать код\nC#");
        api.Requests.Count(r => r.AbsolutePath.StartsWith("/vacancies/")).Should().Be(1);
    }
}
