using JobsPulse.Core.Abstractions;
using JobsPulse.Core.Helpers;
using JobsPulse.Storage.Storages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Vostok.Logging.Microsoft;

namespace JobsPulse.Storage.Infrastructure;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStorage(this IServiceCollection services, IConfiguration config, string connectionStringName)
    {
        var connectionString = config.GetConnectionString(connectionStringName)
                               ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.AddKeyedSingleton<ILoggerFactory>("storage-logger-factory",
            LoggerFactory.Create(lb => lb.AddVostok(FileLogProvider.Create("storage-log")))
        );

        services.AddSingleton(sp =>
        {
            var loggerFactory = sp.GetKeyedService<ILoggerFactory>("storage-logger-factory");

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.UseLoggerFactory(loggerFactory);

            return dataSourceBuilder.Build();
        });

        services.AddDbContextFactory<JobsPulseDbContext>((sp, options) =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();

            var loggerFactory = sp.GetKeyedService<ILoggerFactory>("storage-logger-factory");

            options
                .UseNpgsql(dataSource)
                .UseLoggerFactory(loggerFactory)
                .UseSnakeCaseNamingConvention();
        });

        services.AddSingleton<IStateStore, StateStore>();
        services.AddSingleton<IOutboxStorage, OutboxStorage>();
        services.AddSingleton<IBoardRegistryStorage, BoardRegistryStorage>();

        return services;
    }
}