using JobsPulse.Core.Abstractions;
using JobsPulse.Sources.Lever.Infrastructure;
using JobsPulse.Sources.Lever.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;

namespace JobsPulse.Tests.Integration.Lever;

/// <summary>
/// A container holding the Lever source and nothing else. `Regions` is configured the way `appsettings.json` does it,
/// because the binder appends it to the default list - the duplication the lookup has to survive.
/// </summary>
public sealed class LeverTestHost : IDisposable
{
    private readonly ServiceProvider services;

    public LeverTestHost()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LeverOptions.SectionName}:Regions:0"] = "global",
                [$"{LeverOptions.SectionName}:Regions:1"] = "eu"
            })
            .Build();

        services = new ServiceCollection()
            .AddSingleton<ILog>(new ConsoleLog())
            .AddSingleton(TimeProvider.System)
            .AddLeverSource(config)
            .BuildServiceProvider();
    }

    public IVacancySource Source =>
        services.GetRequiredKeyedService<IVacancySource>(LeverMapper.SourceId);

    public LeverRegionMap Regions => services.GetRequiredService<LeverRegionMap>();

    public void Dispose() => services.Dispose();
}
