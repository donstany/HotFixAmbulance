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
        run.HasIndex(r => new { r.ApiName, r.RequestedAtUtc });
    }
}
