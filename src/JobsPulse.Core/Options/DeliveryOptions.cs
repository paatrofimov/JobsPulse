using System.ComponentModel.DataAnnotations;

namespace JobsPulse.Core.Options;

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    // Telegram: 4096 symbols per message
    [Range(1, 20)] public int VacanciesPerMessage { get; set; } = 8;

    // Telegram throttles ~20 messages per minute
    [Range(0, 60)] public int DelayBetweenMessagesSeconds { get; set; } = 3;

    // Whole windows only - a window bigger than this is the one case sent in parts
    [Range(1, 5000)] public int OutboxBatchSize { get; set; } = 500;

    [Range(1, 300)] public int DispatchOutboxIntervalSeconds { get; set; } = 5;

    [Range(1, 20)] public int MaxAttemptsBeforeDeadLetter { get; set; } = 6;

    // Vacancies published within this window are highlighted in messages
    [Range(0, 365)] public int FreshVacancyDays { get; set; } = 3;

    // Changes detected within one such window are delivered as a single message instead of one per company.
    // Nothing of the window being filled right now is sent until it closes - see DeliveryWindow.
    [Range(1, 1440)] public int GroupChangesWithinMinutes { get; set; } = 15;

    // A closed window is held this long more: a commit stamps its rows before it lands, so a slow one can still
    // arrive into the window after it has closed.
    [Range(0, 300)] public int WindowSettleSeconds { get; set; } = 30;

    // A job whose heartbeat is older than this is dead, not walking - see ITraversalRunStorage.
    [Range(30, 3600)] public int TraversalRunStaleSeconds { get; set; } = 180;


    // How far back the company activity indicator looks - see BoardActivity
    [Range(7, 730)] public int ActivityWindowDays { get; set; } = 90;

    // Delivered notifications are kept for a while for troubleshooting, then dropped
    [Range(0, 8760)] public int DeliveredRetentionHours { get; set; } = 24;

    [Range(1, 1440)] public int CleanupIntervalMinutes { get; set; } = 60;
}