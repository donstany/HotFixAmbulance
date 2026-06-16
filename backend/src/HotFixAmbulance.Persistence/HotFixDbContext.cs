using Microsoft.EntityFrameworkCore;

namespace HotFixAmbulance.Persistence;

public sealed class HotFixDbContext : DbContext
{
    public HotFixDbContext(DbContextOptions<HotFixDbContext> options) : base(options)
    {
    }

    public DbSet<TriageRun> TriageRuns => Set<TriageRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var run = modelBuilder.Entity<TriageRun>();
        run.HasKey(r => r.Id);
        run.Property(r => r.ApiName).HasMaxLength(200).IsRequired();
        run.Property(r => r.ErrorGroupsJson).IsRequired();
        // Store as ISO-8601 TEXT so SQLite can ORDER BY it (DateTimeOffset isn't natively
        // sortable in SQLite EF Core; ISO-8601 strings preserve chronological ordering).
        run.Property(r => r.RequestedAtUtc)
            .HasConversion(
                v => v.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                v => DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind));
        run.HasIndex(r => new { r.ApiName, r.RequestedAtUtc });
    }
}
