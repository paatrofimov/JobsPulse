using System.ComponentModel.DataAnnotations;

namespace JobsPulse.Core.Options;

/// <summary>
/// Настройки поллинга. Живут в appsettings.json, читаются через IOptionsMonitor —
/// смена интервала подхватывается без рестарта (значение перечитывается на каждом круге цикла).
/// </summary>
public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    /// <summary>Базовый период опроса. Может быть переопределён на уровне записи watchlist.</summary>
    [Range(1, 1440)]
    public int IntervalMinutes { get; set; } = 10;

    /// <summary>Сколько бордов обходим параллельно.</summary>
    [Range(1, 32)]
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>Потолок запросов в секунду ко всем источникам суммарно. Вежливость к чужому API.</summary>
    [Range(0.1, 50)]
    public double MaxRequestsPerSecond { get; set; } = 2;

    /// <summary>Таймаут на обход одного борда.</summary>
    [Range(5, 600)]
    public int BoardTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Не отправлять уведомления вообще, только логировать, что бы улетело.
    /// Обязательный режим перед первым включением широкого фильтра.
    /// </summary>
    public bool DryRun { get; set; }
}
