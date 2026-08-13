using System.Net;
using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.HeadHunter;

public sealed class HeadHunterBoardResolverTests
{
    /// <summary>An exact name is answered on its own - there is nothing for the user to choose between.</summary>
    [Test]
    public async Task ResolveByName_should_answer_an_exact_match_alone()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json(
            HeadHunterFixtures.EmployerSearch(
                ("1740", "Яндекс", 900),
                ("87021", "Яндекс Крауд", 40),
                ("3529", "Сбербанк", 5000))));

        using var host = new HeadHunterTestHost(api);

        var candidates = await host.Resolver.ResolveByNameAsync("Яндекс", CancellationToken.None);

        candidates.Should().HaveCount(1);
        candidates[0].BoardId.Should().Be("1740");
        candidates[0].DisplayName.Should().Be("Яндекс");
        candidates[0].JobCount.Should().Be(900);
        candidates[0].BoardUrl.Should().Be("https://hh.ru/employer/1740");
        candidates[0].Resolution.Should().Be(ResolutionKind.DirectSlug);
        // The board id is the employer id, so nothing source-specific has to be carried along with it.
        candidates[0].Configuration.Should().BeNull();
    }

    /// <summary>
    /// Several employers scoring alike is the normal answer of a catalog - a group, its regions, its brands - and
    /// guessing between them would silently watch the wrong company. The whole field is offered instead.
    /// </summary>
    [Test]
    public async Task ResolveByName_should_offer_every_plausible_employer_when_the_answer_is_ambiguous()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json(
            HeadHunterFixtures.EmployerSearch(
                ("1", "Альфа Банк", 500),
                ("2", "Альфа Страхование", 300),
                ("3", "Альфа Капитал", 100))));

        using var host = new HeadHunterTestHost(api);

        var candidates = await host.Resolver.ResolveByNameAsync("Альфа", CancellationToken.None);

        candidates.Should().HaveCountGreaterThan(1);
        candidates.Should().OnlyContain(c => c.Resolution == ResolutionKind.Catalog);
        candidates.Select(c => c.BoardId).Should().OnlyHaveUniqueItems();
    }

    /// <summary>A leader far enough ahead of the runner-up is not ambiguous, even without an exact name.</summary>
    [Test]
    public async Task ResolveByName_should_answer_a_decisive_leader_alone()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json(
            HeadHunterFixtures.EmployerSearch(
                ("1", "Альфа Банк Технологии", 400),
                ("2", "Банк Открытие", 50))));

        using var host = new HeadHunterTestHost(api);

        var candidates = await host.Resolver.ResolveByNameAsync("Альфа Банк", CancellationToken.None);

        // Both are plausible enough to be offered, but one of them is far closer, so it answers alone.
        candidates.Should().HaveCount(1);
        candidates[0].BoardId.Should().Be("1");
        candidates[0].Resolution.Should().Be(ResolutionKind.Catalog);
    }

    /// <summary>
    /// The search is fuzzy on purpose, so its tail holds employers that have nothing to do with the query. Answering
    /// them would put a random company into somebody's watchlist.
    /// </summary>
    [Test]
    public async Task ResolveByName_should_answer_nothing_when_no_employer_is_plausible()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json(
            HeadHunterFixtures.EmployerSearch(("1", "Сбербанк", 5000), ("2", "Пятёрочка", 900))));

        using var host = new HeadHunterTestHost(api);

        var candidates = await host.Resolver.ResolveByNameAsync("Nebius", CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    [Test]
    public async Task ResolveByUrl_should_read_an_employer_link_and_probe_it()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json(
            HeadHunterFixtures.Employer("1740", "Яндекс", 900)));

        using var host = new HeadHunterTestHost(api);

        var candidate = await host.Resolver.ResolveByUrlAsync("https://spb.hh.ru/employer/1740", CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.BoardId.Should().Be("1740");
        candidate.Resolution.Should().Be(ResolutionKind.Catalog);
        api.Requests.Should().HaveCount(1);
        api.Requests[0].AbsolutePath.Should().Be("/employers/1740");
    }

    /// <summary>A vacancy link is how a company is shared, so it has to resolve to the employer behind it.</summary>
    [Test]
    public async Task ResolveByUrl_should_resolve_the_employer_behind_a_vacancy_link()
    {
        var api = new HeadHunterStubApi(uri => uri.AbsolutePath.StartsWith("/vacancies/")
            ? HeadHunterStubAnswer.Json(
                """{ "id": "77", "employer": { "id": "1740", "name": "Яндекс" } }""")
            : HeadHunterStubAnswer.Json(HeadHunterFixtures.Employer("1740", "Яндекс", 900)));

        using var host = new HeadHunterTestHost(api);

        var candidate = await host.Resolver.ResolveByUrlAsync("https://hh.ru/vacancy/77", CancellationToken.None);

        candidate.Should().NotBeNull();
        candidate!.BoardId.Should().Be("1740");
        candidate.Resolution.Should().Be(ResolutionKind.CareersPage);
    }

    /// <summary>Every resolver is asked about every url, and a foreign one must cost no request.</summary>
    [Test]
    public async Task ResolveByUrl_should_ignore_a_url_of_another_platform()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json("{}"));

        using var host = new HeadHunterTestHost(api);

        var candidate = await host.Resolver.ResolveByUrlAsync("https://boards.greenhouse.io/nebius", CancellationToken.None);

        candidate.Should().BeNull();
        api.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Probe_should_answer_nothing_for_an_employer_that_is_gone()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Error(HttpStatusCode.NotFound));

        using var host = new HeadHunterTestHost(api);

        (await host.Resolver.ProbeAsync("999999999", CancellationToken.None)).Should().BeNull();
    }

    /// <summary>The registry stores employer ids, so a token that is not one is rejected before it costs a request.</summary>
    [Test]
    public async Task Probe_should_reject_a_token_that_is_not_an_employer_id()
    {
        var api = new HeadHunterStubApi(_ => HeadHunterStubAnswer.Json("{}"));

        using var host = new HeadHunterTestHost(api);

        (await host.Resolver.ProbeAsync("yandex", CancellationToken.None)).Should().BeNull();
        api.Requests.Should().BeEmpty();
    }
}
