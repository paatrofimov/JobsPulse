using FluentAssertions;
using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Domain.Extensions;
using JobsPulse.Core.Model.Infrastructure;
using JobsPulse.Core.Pipeline;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

public sealed class TelegramSinkTests : IntegrationTestBase
{
    [Test]
    public async Task Sink_should_deliver()
    {
        var sourceTarget = new SourceTarget() { SourceId = "greenhouse", BoardId = "nebius", IncludeDescriptions = true };
        var result = await FetchRealVacanciesAsync(sourceTarget);

        var outboxItems = result.Vacancies
            .Take(15)
            .Select((v, ind) =>
            {
                var changeKind = (VacancyChangeKind)Random.Shared.Next(0, 3);
                var hash = VacancyHasher.Compute(v);
                var companyIndex = Random.Shared.Next(0, 3);

                return new OutboxItem()
                {
                    Id = ind,
                    ChangeKind = changeKind,
                    CompanyName = $"TEST COMPANY {companyIndex}",
                    Vacancy = v,
                    DedupKey = v.ToDedupKey(changeKind, hash)
                };
            })
            .ToArray();

        using var cts = new CancellationTokenSource(RequestTimeout);
        var deliveryResult = await VacancySink.DeliverAsync(outboxItems, cts.Token);

        deliveryResult.Success.Should().BeTrue();
        deliveryResult.Error.Should().BeNull();
        deliveryResult.RetryAfter.Should().BeNull();
    }
}