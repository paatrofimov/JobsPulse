using JobsPulse.Storage.Storages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobsPulse.Storage.Infrastructure;

/// <summary>
/// Only for `dotnet ef migrations` - the tool needs a context without the host and without a real database.
/// The runtime context always comes from <see cref="StorageServiceCollectionExtensions.AddStorage"/>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JobsPulseDbContext>
{
    public JobsPulseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<JobsPulseDbContext>()
            .UseNpgsql("Host=localhost;Database=jobspulse;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new JobsPulseDbContext(options);
    }
}
