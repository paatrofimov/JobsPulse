using JobsPulse.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobsPulse.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddSqliteStorage(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<StorageOptions>().Bind(config.GetSection(StorageOptions.SectionName));

        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<SqliteStateStore>();
        services.AddSingleton<SqliteOutbox>();
        services.AddSingleton<IStateStore>(sp => sp.GetRequiredService<SqliteStateStore>());
        services.AddSingleton<IOutbox>(sp => sp.GetRequiredService<SqliteOutbox>());

        return services;
    }
}
