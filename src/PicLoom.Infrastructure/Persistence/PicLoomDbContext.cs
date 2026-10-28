using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PicLoom.Core;

namespace PicLoom.Infrastructure.Persistence;

public sealed class PicLoomDbContext : DbContext
{
    public PicLoomDbContext(DbContextOptions<PicLoomDbContext> options) : base(options)
    {
    }

    public DbSet<PromptTemplate> Templates => Set<PromptTemplate>();
    public DbSet<ResolutionProfile> Resolutions => Set<ResolutionProfile>();
    public DbSet<TaskGroup> Groups => Set<TaskGroup>();
    public DbSet<GenerationTask> Tasks => Set<GenerationTask>();
    public DbSet<JobAttempt> Attempts => Set<JobAttempt>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<ReferenceImage> References => Set<ReferenceImage>();
    public DbSet<ProviderSettings> ProviderSettings => Set<ProviderSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTemplates(modelBuilder);
        ConfigureResolutions(modelBuilder);
        ConfigureGroups(modelBuilder);
        ConfigureTasks(modelBuilder);
        ConfigureAttempts(modelBuilder);
        ConfigureAssets(modelBuilder);
        ConfigureReferences(modelBuilder);
        ConfigureProviderSettings(modelBuilder);
        ApplyUtcDateTimeConversions(modelBuilder);
    }

    private static void ConfigureTemplates(ModelBuilder modelBuilder)
    {
        var template = modelBuilder.Entity<PromptTemplate>();
        template.ToTable("Templates");
        template.HasKey(x => x.Id);
        template.Property(x => x.Name).HasMaxLength(120).IsRequired();
        template.Property(x => x.Body).HasMaxLength(20000).IsRequired();

        var tags = template.Property(x => x.Tags)
            .HasConversion(new JsonValueConverter<string[]>())
            .HasColumnType("TEXT")
            .IsRequired();
        tags.Metadata.SetValueComparer(JsonValues.TagsComparer);

        template.Property(x => x.Revision).IsRequired();
        template.Property(x => x.CreatedAt).IsRequired();
        template.Property(x => x.UpdatedAt).IsRequired();
        template.HasIndex(x => x.CreatedAt);
    }

    private static void ConfigureResolutions(ModelBuilder modelBuilder)
    {
        var resolution = modelBuilder.Entity<ResolutionProfile>();
        resolution.ToTable("Resolutions");
        resolution.HasKey(x => x.Id);
        resolution.Property(x => x.Name).HasMaxLength(120).IsRequired();
        resolution.Property(x => x.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
        resolution.Property(x => x.AspectRatio).HasMaxLength(16);
        resolution.Property(x => x.ImageSize).HasMaxLength(8);
        resolution.Property(x => x.CreatedAt).IsRequired();
        resolution.Property(x => x.UpdatedAt).IsRequired();
        resolution.HasIndex(x => x.CreatedAt);
    }

    private static void ConfigureGroups(ModelBuilder modelBuilder)
    {
        var group = modelBuilder.Entity<TaskGroup>();
        group.ToTable("Groups");
        group.HasKey(x => x.Id);
        group.Property(x => x.Name).HasMaxLength(120).IsRequired();
        group.Property(x => x.IsSystem).IsRequired();
        group.Property(x => x.CreatedAt).IsRequired();
        group.Property(x => x.UpdatedAt).IsRequired();
        group.HasIndex(x => x.CreatedAt);

        // The built-in "ungrouped" bucket. Inserted once by the ByokQuickstart migration; user
        // created groups always carry IsSystem = false.
        group.HasData(new
        {
            Id = ReservedIds.UngroupedGroup,
            Name = ReservedIds.UngroupedGroupName,
            IsSystem = true,
            CreatedAt = ReservedIds.SeedTimestamp,
            UpdatedAt = ReservedIds.SeedTimestamp
        });
    }

    private static void ConfigureTasks(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<GenerationTask>();
        task.ToTable("Tasks");
        task.HasKey(x => x.Id);

        task.Property(x => x.GroupId).IsRequired();
        task.HasOne<TaskGroup>()
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        var prompt = task.Property(x => x.PromptSnapshot)
            .HasConversion(new JsonValueConverter<PromptSnapshot>())
            .HasColumnType("TEXT")
            .IsRequired();
        prompt.Metadata.SetValueComparer(JsonValues.PromptSnapshotComparer);

        var resolution = task.Property(x => x.ResolutionSnapshot)
            .HasConversion(new JsonValueConverter<ResolutionSnapshot>())
            .HasColumnType("TEXT")
            .IsRequired();
        resolution.Metadata.SetValueComparer(JsonValues.ResolutionSnapshotComparer);

        // Additive: null on rows written before image inputs existed.
        task.Property(x => x.Options)
            .HasConversion(new JsonNullableValueConverter<GenerationOptions>())
            .HasColumnType("TEXT");
        task.Property(x => x.ActualCount);
        task.Property(x => x.ExpectedReferenceCount).IsRequired();

        task.Property(x => x.Provider).HasMaxLength(80).IsRequired();
        task.Property(x => x.Model).HasMaxLength(120).IsRequired();
        task.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .IsConcurrencyToken();
        task.Property(x => x.ErrorCode).HasMaxLength(64);
        task.Property(x => x.CreatedAt).IsRequired();
        task.Property(x => x.UpdatedAt).IsRequired();

        task.HasMany(x => x.Assets)
            .WithOne()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        task.HasIndex(x => new { x.Status, x.CreatedAt });
        task.HasIndex(x => x.GroupId);
    }

    private static void ConfigureAttempts(ModelBuilder modelBuilder)
    {
        var attempt = modelBuilder.Entity<JobAttempt>();
        attempt.ToTable("Attempts");
        attempt.HasKey(x => x.Id);

        attempt.HasOne<GenerationTask>()
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // P0 deliberately allows exactly one dispatch attempt per task.
        attempt.HasIndex(x => x.TaskId).IsUnique();
        attempt.HasIndex(x => x.IdempotencyKey).IsUnique();

        attempt.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        attempt.Property(x => x.RemoteId).HasMaxLength(256);
        attempt.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired()
            .IsConcurrencyToken();
        attempt.Property(x => x.ErrorCode).HasMaxLength(64);
        attempt.Property(x => x.DispatchedAt).IsRequired();
    }

    private static void ConfigureAssets(ModelBuilder modelBuilder)
    {
        var asset = modelBuilder.Entity<Asset>();
        asset.ToTable("Assets");
        asset.HasKey(x => x.Id);

        asset.HasOne<JobAttempt>()
            .WithMany()
            .HasForeignKey(x => x.AttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        asset.Property(x => x.RelativePath).HasMaxLength(1024).IsRequired();
        asset.Property(x => x.ThumbnailPath).HasMaxLength(1024).IsRequired();
        asset.Property(x => x.MimeType).HasMaxLength(128).IsRequired();
        asset.Property(x => x.Hash).HasMaxLength(64).IsRequired();
        asset.Property(x => x.Ordinal).IsRequired();
        asset.Property(x => x.CreatedAt).IsRequired();

        // A batch may legitimately contain identical bytes (a relay may answer the same image twice),
        // so uniqueness is defined by position inside the batch rather than by content.
        asset.HasIndex(x => new { x.AttemptId, x.Ordinal }).IsUnique();
        asset.HasIndex(x => x.TaskId);
    }

    private static void ConfigureReferences(ModelBuilder modelBuilder)
    {
        var reference = modelBuilder.Entity<ReferenceImage>();
        reference.ToTable("ReferenceImages");
        reference.HasKey(x => x.Id);

        // Nullable while the image is a draft; cascade removes the files' rows with their task.
        // TaskId doubles as an optimistic concurrency token: binding an image is the conditional
        // update "... WHERE TaskId IS NULL", so two concurrent tasks cannot both claim it.
        reference.HasOne<GenerationTask>()
            .WithMany(x => x.References)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        reference.Property(x => x.TaskId).IsConcurrencyToken();

        reference.Property(x => x.RelativePath).HasMaxLength(1024).IsRequired();
        reference.Property(x => x.ThumbnailPath).HasMaxLength(1024).IsRequired();
        reference.Property(x => x.MimeType).HasMaxLength(128).IsRequired();
        reference.Property(x => x.Source).HasMaxLength(16).IsRequired();
        reference.Property(x => x.Ordinal).IsRequired();
        reference.Property(x => x.CreatedAt).IsRequired();

        // Unbound rows (TaskId null) are not constrained: SQLite treats every NULL as distinct.
        reference.HasIndex(x => new { x.TaskId, x.Ordinal });
    }

    private static void ConfigureProviderSettings(ModelBuilder modelBuilder)
    {
        var settings = modelBuilder.Entity<ProviderSettings>();
        settings.ToTable("ProviderSettings");
        settings.HasKey(x => x.Id);
        settings.Property(x => x.Protocol).HasConversion<string>().HasMaxLength(32).IsRequired();
        settings.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        settings.Property(x => x.BaseUrl).HasMaxLength(512).IsRequired();
        settings.Property(x => x.Model).HasMaxLength(120).IsRequired();
        settings.Property(x => x.KeyOrigin).HasMaxLength(256).IsRequired();
        settings.Property(x => x.MaskedKey).HasMaxLength(32);
        settings.Property(x => x.HasKey).IsRequired();
        settings.Property(x => x.IsEnabled).IsRequired();
        settings.Property(x => x.CreatedAt).IsRequired();
        settings.Property(x => x.UpdatedAt).IsRequired();
        // P2-1 will add profiles; today one row per protocol.
        settings.HasIndex(x => x.Protocol).IsUnique();
    }

    private static void ApplyUtcDateTimeConversions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(new UtcDateTimeConverter());
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(new UtcNullableDateTimeConverter());
            }
        }
    }
}
