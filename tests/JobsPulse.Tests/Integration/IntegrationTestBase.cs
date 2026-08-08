using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Pipeline;
using JobsPulse.Host.Rouitines;
using JobsPulse.Sinks.Telegram.Infrastructure;
using JobsPulse.Sources.Greenhouse.Infrastructure;
using JobsPulse.Sources.Greenhouse.Options;
using JobsPulse.Storage.Infrastructure;
using JobsPulse.Storage.Storages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobsPulse.Tests.Integration;

public abstract partial class IntegrationTestBase : IDisposable
{
    private readonly ServiceProvider _services;

    protected IntegrationTestBase()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<PollingWorker>()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{GreenhouseOptions.SectionName}:BaseUrl"] = "https://boards-api.greenhouse.io/v1/boards/",
                [$"{GreenhouseOptions.SectionName}:IncludeContentOnPoll"] = "false"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(logging => logging
            .AddSimpleConsole()
            .SetMinimumLevel(LogLevel.Debug));

        services
            .AddSingleton(TimeProvider.System)
            .AddSingleton<VacancyMatcher>()
            .AddGreenhouseSource(config)
            .AddTelegramSink(config)
            .AddStorage(config, connectionStringName: "PostgresTest");

        _services = services.BuildServiceProvider();
    }

    // Upper bound for a single live call
    protected static TimeSpan RequestTimeout => TimeSpan.FromSeconds(60);

    protected VacancyMatcher VacancyMatcher => _services.GetRequiredService<VacancyMatcher>();

    protected IVacancySink VacancySink => _services.GetRequiredService<IVacancySink>();

    protected IStateStore StateStore => _services.GetRequiredService<IStateStore>();

    protected IDbContextFactory<JobsPulseDbContext> DbContextFactory => _services.GetRequiredService<IDbContextFactory<JobsPulseDbContext>>();

    public void Dispose()
    {
        _services.Dispose();
    }

    protected IVacancySource GetVacancySource(string sourceId)
    {
        return _services.GetRequiredKeyedService<IVacancySource>(sourceId);
    }
}