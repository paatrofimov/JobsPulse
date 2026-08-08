using FluentAssertions;
using JobsPulse.Core.Model.Infrastructure;
using NUnit.Framework;

namespace JobsPulse.Tests.Integration.Telegram;

public sealed class TelegramSinkTests : IntegrationTestBase
{
    [Test]
    public async Task Sink_should_deliver()
    {
        var sourceTarget = new SourceTarget() { SourceId = "greenhouse", BoardId = "nebius", IncludeDescriptions = true };
        var result = await FetchRealVacanciesAsync(sourceTarget);

        var vacancies = result.Vacancies.Take(15).ToArray();

        var outboxItems = MapVacanciesToMockOutboxes(vacancies);

        using var cts = new CancellationTokenSource(RequestTimeout);
        var deliveryResult = await VacancySink.DeliverAsync(outboxItems, cts.Token);

        deliveryResult.Success.Should().BeTrue();
        deliveryResult.Error.Should().BeNull();
        deliveryResult.RetryAfter.Should().BeNull();
    }
}