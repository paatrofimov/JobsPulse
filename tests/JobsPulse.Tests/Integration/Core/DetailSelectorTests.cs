using FluentAssertions;
using JobsPulse.Core.Infrastructure;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Core;

/// <summary>
/// Which postings cost a detail request. The point is that a steady board asks for nothing, and that a first sight of
/// a big board is capped instead of timing out on every cycle.
/// </summary>
public sealed class DetailSelectorTests
{
    private static readonly Func<Vacancy, Vacancy, bool> SameTitle = (listed, known) => listed.Title == known.Title;

    [Test]
    public void Select_should_reuse_known_postings_with_unchanged_list_data()
    {
        var known = Vacancy("1", "Engineer");
        var target = Target(known);

        var decisions = DetailSelector.Select([Vacancy("1", "Engineer")], target, false, 10, SameTitle);

        decisions.Should().Equal(DetailDecision.Reuse);
    }

    [Test]
    public void Select_should_fetch_new_and_changed_postings()
    {
        var target = Target(Vacancy("1", "Engineer"));

        var decisions = DetailSelector.Select(
            [Vacancy("1", "Senior Engineer"), Vacancy("2", "Designer")], target, false, 10, SameTitle);

        decisions.Should().Equal(DetailDecision.Fetch, DetailDecision.Fetch);
    }

    [Test]
    public void Select_should_cap_fetches_by_budget()
    {
        var listed = Enumerable.Range(0, 5).Select(i => Vacancy(i.ToString(), $"Job {i}")).ToList();

        var decisions = DetailSelector.Select(listed, Target(), false, 2, SameTitle);

        decisions.Count(d => d == DetailDecision.Fetch).Should().Be(2);
        decisions.Count(d => d == DetailDecision.ListOnly).Should().Be(3);
    }

    [Test]
    public void Select_should_not_spend_budget_on_postings_no_filter_can_store()
    {
        var target = Target() with { MayBeStored = v => v.Title.Contains("Engineer") };

        var decisions = DetailSelector.Select(
            [Vacancy("1", "Accountant"), Vacancy("2", "Engineer")], target, false, 1, SameTitle);

        decisions.Should().Equal(DetailDecision.ListOnly, DetailDecision.Fetch);
    }

    [Test]
    public void Select_should_fetch_every_plausible_posting_without_budget_when_descriptions_are_needed()
    {
        var target = Target(Vacancy("1", "Engineer")) with
        {
            NeedsDescription = true,
            MayBeStored = v => v.Title.Contains("Engineer")
        };

        var decisions = DetailSelector.Select(
            [Vacancy("1", "Engineer"), Vacancy("2", "Data Engineer"), Vacancy("3", "Accountant")],
            target, false, 0, SameTitle);

        decisions.Should().Equal(DetailDecision.Fetch, DetailDecision.Fetch, DetailDecision.ListOnly);
    }

    [Test]
    public void Select_should_fetch_everything_when_content_is_forced()
    {
        var target = Target(Vacancy("1", "Engineer")) with { MayBeStored = _ => false };

        var decisions = DetailSelector.Select(
            [Vacancy("1", "Engineer"), Vacancy("2", "Designer")], target, true, 0, SameTitle);

        decisions.Should().Equal(DetailDecision.Fetch, DetailDecision.Fetch);
    }

    [Test]
    public async Task FetchAsync_should_request_only_selected_postings()
    {
        DetailDecision[] decisions = [DetailDecision.Fetch, DetailDecision.Reuse, DetailDecision.ListOnly, DetailDecision.Fetch];
        var requested = new List<int>();

        var details = await DetailSelector.FetchAsync(decisions, 2, (i, _) =>
        {
            lock (requested)
                requested.Add(i);

            return Task.FromResult<string?>($"detail {i}");
        }, CancellationToken.None);

        requested.Should().BeEquivalentTo([0, 3]);
        details.Should().Equal("detail 0", null, null, "detail 3");
    }

    private static SourceTarget Target(params Vacancy[] known) => new()
    {
        SourceId = "test",
        BoardId = "board",
        Known = known.ToDictionary(v => v.PostId)
    };

    private static Vacancy Vacancy(string postId, string title) => new()
    {
        SourceId = "test",
        BoardId = "board",
        PostId = postId,
        Title = title,
        Url = $"https://example.com/{postId}"
    };
}
