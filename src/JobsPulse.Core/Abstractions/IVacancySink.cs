using JobsPulse.Core.Model.Domain;
using JobsPulse.Core.Model.Infrastructure;

namespace JobsPulse.Core.Abstractions;

public interface IVacancySink
{
    Task<DeliveryResult> DeliverAsync(IReadOnlyList<OutboxItem> batch, CancellationToken ct);
}