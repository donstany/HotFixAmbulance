using Microsoft.EntityFrameworkCore;

namespace DemoApi;

public sealed class DemoApiDbContext(DbContextOptions<DemoApiDbContext> options) : DbContext(options)
{
    public DbSet<InvoiceRecord> Invoices => Set<InvoiceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InvoiceRecord>(b =>
        {
            b.ToTable("Invoices");
            b.HasKey(x => x.Id);
            b.Property(x => x.InvoiceNumber).IsRequired();
            b.HasIndex(x => x.InvoiceNumber).IsUnique();
            b.Property(x => x.CustomerId).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);
        });
    }
}

public sealed class InvoiceRecord
{
    public int Id { get; set; }
    public required string InvoiceNumber { get; set; }
    public required string CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Produces realistic persistence failures for demo traffic.
/// </summary>
public sealed class DatabaseFailureSimulator
{
    private readonly DemoApiDbContext _db;

    public DatabaseFailureSimulator(DemoApiDbContext db) => _db = db;

    public async Task TriggerDuplicateInvoiceAsync(CancellationToken ct)
    {
        var invoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMddHHmmss}";

        _db.Invoices.Add(new InvoiceRecord
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = "c-001",
            Amount = 125.50m,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        _db.Invoices.Add(new InvoiceRecord
        {
            InvoiceNumber = invoiceNumber,
            CustomerId = "c-002",
            Amount = 62.00m,
            CreatedAtUtc = DateTime.UtcNow,
        });

        // Unique index violation from a real SQLite-backed EF Core write.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public void TriggerQueryTimeout()
    {
        throw new TimeoutException(
            "Execution Timeout Expired while querying productiondatabase.InvoiceProjection; waited for lock resources.");
    }
}
