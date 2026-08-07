using System.ComponentModel.DataAnnotations;

namespace JobsPulse.Core.Options;

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    // Telegram: 4096 symbols per message
    [Range(1, 20)] public int VacanciesPerMessage { get; set; } = 8;

    // Telegram throttles ~20 messages per minute
    [Range(0, 60)] public int DelayBetweenMessagesSeconds { get; set; } = 3;

    [Range(1, 500)] public int OutboxBatchSize { get; set; } = 50;

    [Range(1, 300)] public int DispatchOutboxIntervalSeconds { get; set; } = 5;

    [Range(1, 20)] public int MaxAttemptsBeforeDeadLetter { get; set; } = 6;
}