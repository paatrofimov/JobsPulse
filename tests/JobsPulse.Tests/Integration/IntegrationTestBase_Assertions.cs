using FluentAssertions;
using FluentAssertions.Equivalency;
using JobsPulse.Core.Model.Domain;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration;

[TestFixture]
public abstract partial class IntegrationTestBase
{
    protected static EquivalencyOptions<Vacancy> ConfigureVacancyComparisonOpts(EquivalencyOptions<Vacancy> opts)
    {
        return opts
            .Excluding(x => x.UpdatedAt)
            .Excluding(x => x.FirstSeenAt)
            .Excluding(x => x.ContentHash)
            .Excluding(x => x.Description);
    }

    protected static EquivalencyOptions<OutboxItem> ConfigureOutboxComparisonOpts(EquivalencyOptions<OutboxItem> opts)
    {
        return opts
            .Excluding(x => x.Id)
            .Excluding(x => x.Vacancy.UpdatedAt)
            .Excluding(x => x.Vacancy.FirstSeenAt)
            .Excluding(x => x.Vacancy.ContentHash)
            .Excluding(x => x.Vacancy.Description);
    }

    protected static void AssertEquivalencyOutboxes(IReadOnlyList<OutboxItem> a, IReadOnlyList<OutboxItem> b)
    {
        a.Should().HaveCount(b.Count);
        a.Should().BeEquivalentTo(b, ConfigureOutboxComparisonOpts);
    }

    protected static void AssertEquivalentVacancies(IReadOnlyList<Vacancy> a, IReadOnlyList<Vacancy> b)
    {
        a.Should().HaveCount(b.Count);
        a.Should().BeEquivalentTo(b, ConfigureVacancyComparisonOpts);
    }
}