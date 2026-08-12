using JobsPulse.Storage.PersistentModels;
using Microsoft.EntityFrameworkCore;

namespace JobsPulse.Storage.Storages;

public class JobsPulseDbContext(
    DbContextOptions<JobsPulseDbContext> options)
    : DbContext(options)
{
    public DbSet<PersistentSeenVacancy> SeenVacancies => Set<PersistentSeenVacancy>();
    public DbSet<PersistentOutboxItem> Outbox => Set<PersistentOutboxItem>();
    public DbSet<PersistentBoardRegistryEntry> BoardRegistry => Set<PersistentBoardRegistryEntry>();
    public DbSet<PersistentCrawlIndexState> CrawlIndexState => Set<PersistentCrawlIndexState>();
    public DbSet<PersistentWatchlist> Watchlists => Set<PersistentWatchlist>();
    public DbSet<PersistentWatchlistEntry> WatchlistEntries => Set<PersistentWatchlistEntry>();
    public DbSet<PersistentWatchlistVacancy> WatchlistVacancies => Set<PersistentWatchlistVacancy>();
    public DbSet<PersistentBotUser> BotUsers => Set<PersistentBotUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureBotUser(modelBuilder);
        ConfigureSeenVacancy(modelBuilder);
        ConfigureOutbox(modelBuilder);
        ConfigureBoardRegistry(modelBuilder);
        ConfigureCrawlIndexState(modelBuilder);
        ConfigureWatchlist(modelBuilder);
        ConfigureWatchlistEntry(modelBuilder);
        ConfigureWatchlistVacancy(modelBuilder);
    }

    private static void ConfigureBotUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentBotUser>();

        entity.ToTable("bot_user");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .UseIdentityByDefaultColumn();

        // The telegram user id is the identity a watchlist owner is stored as.
        entity.HasIndex(x => x.TelegramUserId)
            .IsUnique();
    }

    private static void ConfigureBoardRegistry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentBoardRegistryEntry>();

        entity.ToTable("board_registry");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .UseIdentityByDefaultColumn();

        // ON CONFLICT target - the logical identity of a board.
        entity.HasIndex(x => new
            {
                x.SourceId,
                x.BoardId
            })
            .IsUnique();

        entity.Property(x => x.Configuration)
            .HasColumnType("jsonb");
    }

    private static void ConfigureCrawlIndexState(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentCrawlIndexState>();

        entity.ToTable("crawl_index_state");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .UseIdentityByDefaultColumn();

        // One row per (source, crawl index) - a processed index is never read again.
        entity.HasIndex(x => new
            {
                x.SourceId,
                x.CollectionId
            })
            .IsUnique();
    }

    private static void ConfigureSeenVacancy(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentSeenVacancy>();

        entity.ToTable("seen_vacancy");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .UseIdentityByDefaultColumn();

        // ON CONFLICT target + logical identity of vacancy.
        entity.HasIndex(x => new
            {
                x.SourceId,
                x.BoardId,
                x.PostId
            })
            .IsUnique();

        // Fast loading of active vacancies for a board.
        entity.HasIndex(x => new
            {
                x.SourceId,
                x.BoardId
            })
            .HasFilter("closed_at IS NULL");

        entity.Property(x => x.Offices)
            .HasColumnType("text[]");
    }

    private static void ConfigureOutbox(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentOutboxItem>();

        entity.ToTable("outbox");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .UseIdentityByDefaultColumn();

        // ON CONFLICT (dedup_key)
        entity.HasIndex(x => x.DedupKey)
            .IsUnique();

        // dispatcher lookup
        entity.HasIndex(x => new
        {
            x.Status,
            x.NextAttemptAt
        });

        entity.Property(x => x.VacancyPayload)
            .HasColumnType("jsonb");
    }

    private static void ConfigureWatchlist(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentWatchlist>();

        entity.ToTable("watchlist");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .UseIdentityByDefaultColumn();

        // A watchlist is addressed by name from the bot, so the name is the second identity.
        entity.HasIndex(x => x.Name)
            .IsUnique();

        // «My watchlists» is the most frequent read of the bot.
        entity.HasIndex(x => x.OwnerUserId);

        entity.Property(x => x.Filter)
            .HasColumnType("jsonb");

        entity.HasMany(x => x.Entries)
            .WithOne(x => x.Watchlist)
            .HasForeignKey(x => x.WatchlistId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureWatchlistEntry(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentWatchlistEntry>();

        entity.ToTable("watchlist_entry");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .UseIdentityByDefaultColumn();

        // One board is listed once per watchlist, but may be listed in many watchlists.
        entity.HasIndex(x => new
            {
                x.WatchlistId,
                x.SourceId,
                x.BoardId
            })
            .IsUnique();

        entity.Property(x => x.Configuration)
            .HasColumnType("jsonb");
    }

    private static void ConfigureWatchlistVacancy(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PersistentWatchlistVacancy>();

        entity.ToTable("watchlist_vacancy");

        entity.HasKey(x => x.Id);

        entity.Property(x => x.Id)
            .UseIdentityByDefaultColumn();

        // ON CONFLICT target - one match row per (watchlist, vacancy).
        entity.HasIndex(x => new
            {
                x.WatchlistId,
                x.SourceId,
                x.BoardId,
                x.PostId
            })
            .IsUnique();

        // The polling cycle reads the match layer of one board at a time.
        entity.HasIndex(x => new
        {
            x.SourceId,
            x.BoardId
        });

        entity.HasOne(x => x.Watchlist)
            .WithMany()
            .HasForeignKey(x => x.WatchlistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
