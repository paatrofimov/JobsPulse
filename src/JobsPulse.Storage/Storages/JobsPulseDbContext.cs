using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;

namespace JobsPulse.Storage.Storages;

internal class JobsPulseDbContext(
    DbContextOptions<JobsPulseDbContext> options)
    : DbContext(options)
{
    public DbSet<PersistentSeenVacancy> SeenVacancies => Set<PersistentSeenVacancy>();
    public DbSet<PersistentOutboxItem> Outbox => Set<PersistentOutboxItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSeenVacancy(modelBuilder);
        ConfigureOutbox(modelBuilder);
    }

    private static void ConfigureSeenVacancy(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentSeenVacancy>();

        entity.ToTable("seen_vacancy");

        entity.HasKey(x => new
        {
            x.SourceId,
            x.BoardId,
            x.PostId
        });

        entity.HasIndex(x => new
            {
                x.SourceId, x.BoardId
            })
            .HasFilter("closed_at IS NULL");

        entity.Property(x => x.VacancyPayload)
            .HasColumnType("jsonb");
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentOutboxItem>();

        entity.ToTable("outbox");

        entity.HasKey(x => x.Id);

        entity.HasIndex(x => new
        {
            x.Status,
            x.NextAttemptAt
        });

        entity.Property(x => x.VacancyPayload)
            .HasColumnType("jsonb");
    }
}