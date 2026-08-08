using JobsPulse.Core.Abstractions;
using JobsPulse.Storage.Storages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JobsPulse.Storage.Infrastructure;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddStorage(this IServiceCollection services, IConfiguration config, string connectionStringName)
    {
        var connectionString = config.GetConnectionString(connectionStringName)
                               ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");

        services.AddNpgsqlDataSource(connectionString);

        services.AddDbContextFactory<JobsPulseDbContext>((sp, options) =>
        {
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();

            options
                .UseNpgsql(dataSource)
                .UseSnakeCaseNamingConvention();
        });

        services.AddSingleton<IStateStore, StateStore>();
        services.AddSingleton<IOutboxStorage, OutboxStorage>();

        return services;
    }
}