using System.ComponentModel.DataAnnotations;

namespace JobsPulse.Core.Options;

public sealed class DeliveryOptions
{
    public const string SectionName = "Delivery";

    /// <summary>Сколько вакансий склеиваем в одно сообщение. Telegram: 4096 символов на сообщение.</summary>
    [Range(1, 20)]
    public int VacanciesPerMessage { get; set; } = 8;

    /// <summary>Пауза между сообщениями в один чат. Telegram душит на ~20 сообщениях в минуту в группу.</summary>
    [Range(0, 60)]
    public int DelayBetweenMessagesSeconds { get; set; } = 3;

    /// <summary>Сколько элементов outbox берём за один проход диспетчера.</summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;

    /// <summary>Как часто диспетчер проверяет outbox.</summary>
    [Range(1, 300)]
    public int DispatchIntervalSeconds { get; set; } = 5;

    /// <summary>После скольких неудач элемент уходит в dead-letter.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 6;

    /// <summary>Предохранитель от спама: потолок сообщений в сутки на чат.</summary>
    [Range(1, 10_000)]
    public int DailyMessageCap { get; set; } = 200;
}
