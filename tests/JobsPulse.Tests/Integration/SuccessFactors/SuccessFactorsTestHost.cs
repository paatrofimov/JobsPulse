using JobsPulse.Core.Abstractions;
using JobsPulse.Sources.SuccessFactors.Infrastructure;
using JobsPulse.Sources.SuccessFactors.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;

namespace JobsPulse.Tests.Integration.SuccessFactors;

/// <summary>
/// A container holding the source and nothing else. `IntegrationTestBase` brings storage and the telegram sink with
/// it, and neither has anything to do with reading a career site - these tests only talk to the sites.
/// </summary>
public sealed class SuccessFactorsTestHost : IDisposable
{
    private readonly ServiceProvider services;

    public SuccessFactorsTestHost(params (string Key, string Value)[] overrides)
    {
        var settings = overrides.ToDictionary(
            o => $"{SuccessFactorsOptions.SectionName}:{o.Key}",
            o => (string?)o.Value);

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        services = new ServiceCollection()
            .AddSingleton<ILog>(new ConsoleLog())
            .AddSuccessFactorsSource(config)
            .BuildServiceProvider();
    }

    public IVacancySource Source =>
        services.GetRequiredKeyedService<IVacancySource>(SuccessFactorsMapper.SourceId);

    public IBoardResolver Resolver =>
        services.GetRequiredKeyedService<IBoardResolver>(SuccessFactorsMapper.SourceId);

    public void Dispose() => services.Dispose();
}
