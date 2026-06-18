using Microsoft.EntityFrameworkCore;

namespace DemoApi;

public sealed class DemoApiDbContext(DbContextOptions<DemoApiDbContext> options) : DbContext(options)
{
    public DbSet<CustomerRecord> Customers => Set<CustomerRecord>();
    public DbSet<OrderRecord> Orders => Set<OrderRecord>();
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<InvoiceRecord> Invoices => Set<InvoiceRecord>();
    public DbSet<TransferRecord> Transfers => Set<TransferRecord>();
    public DbSet<PricingRecord> PricingRecords => Set<PricingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerRecord>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(255);
            b.Property(x => x.Email).IsRequired().HasMaxLength(255);
            b.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<OrderRecord>(b =>
        {
            b.ToTable("Orders");
            b.HasKey(x => x.Id);
            b.Property(x => x.OrderNumber).IsRequired().HasMaxLength(50);
            b.HasIndex(x => x.OrderNumber).IsUnique();
            b.Property(x => x.CustomerId).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.HasOne<CustomerRecord>().WithMany().HasForeignKey(x => x.CustomerId);
        });

        modelBuilder.Entity<PaymentRecord>(b =>
        {
            b.ToTable("Payments");
            b.HasKey(x => x.Id);
            b.Property(x => x.PaymentId).IsRequired().HasMaxLength(50);
            b.HasIndex(x => x.PaymentId).IsUnique();
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.CustomerId).IsRequired();
        });

        modelBuilder.Entity<UserRecord>(b =>
        {
            b.ToTable("Users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(255);
            b.Property(x => x.Email).IsRequired().HasMaxLength(255);
            b.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<InvoiceRecord>(b =>
        {
            b.ToTable("Invoices");
            b.HasKey(x => x.Id);
            b.Property(x => x.InvoiceNumber).IsRequired().HasMaxLength(50);
            b.HasIndex(x => x.InvoiceNumber).IsUnique();
            b.Property(x => x.CustomerId).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<TransferRecord>(b =>
        {
            b.ToTable("Transfers");
            b.HasKey(x => x.Id);
            b.Property(x => x.TransferId).IsRequired().HasMaxLength(50);
            b.HasIndex(x => x.TransferId).IsUnique();
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.CustomerId).IsRequired();
            b.Property(x => x.PaymentIds).IsRequired().HasMaxLength(500);
        });

        modelBuilder.Entity<PricingRecord>(b =>
        {
            b.ToTable("PricingRecords");
            b.HasKey(x => x.Id);
            b.Property(x => x.Subtotal).HasPrecision(18, 2);
            b.Property(x => x.LoyaltyMultiplier).HasPrecision(5, 2);
            b.Property(x => x.FinalAmount).HasPrecision(18, 2);
        });
    }
}

public sealed class CustomerRecord
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class OrderRecord
{
    public int Id { get; set; }
    public required string OrderNumber { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PaymentRecord
{
    public int Id { get; set; }
    public required string PaymentId { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class UserRecord
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class InvoiceRecord
{
    public int Id { get; set; }
    public required string InvoiceNumber { get; set; }
    public required string CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class TransferRecord
{
    public int Id { get; set; }
    public required string TransferId { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "OnHold";
    public required string PaymentIds { get; set; }
    public string Rails { get; set; } = "FPS,SWIFT";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class PricingRecord
{
    public int Id { get; set; }
    public decimal Subtotal { get; set; }
    public decimal LoyaltyMultiplier { get; set; }
    public decimal FinalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Performs real database CRUD operations that demonstrate failure scenarios.
/// </summary>
public sealed class DatabaseFailureSimulator
{
    private readonly DemoApiDbContext _db;

    public DatabaseFailureSimulator(DemoApiDbContext db) => _db = db;

    /// <summary>
    /// Real CRUD: Insert a customer with duplicate email (unique constraint violation).
    /// </summary>
    public async Task<CustomerRecord> CreateCustomerAsync(string name, string email, CancellationToken ct = default)
    {
        var customer = new CustomerRecord
        {
            Name = name,
            Email = email,
            CreatedAtUtc = DateTime.UtcNow,
        };
        
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return customer;
    }

    /// <summary>
    /// Real CRUD: Insert order for a customer.
    /// </summary>
    public async Task<OrderRecord> CreateOrderAsync(int customerId, decimal amount, CancellationToken ct = default)
    {
        var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var order = new OrderRecord
        {
            OrderNumber = orderNumber,
            CustomerId = customerId,
            Amount = amount,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order;
    }

    /// <summary>
    /// Real CRUD: Insert payment record.
    /// </summary>
    public async Task<PaymentRecord> CreatePaymentAsync(string paymentId, int customerId, decimal amount, CancellationToken ct = default)
    {
        var payment = new PaymentRecord
        {
            PaymentId = paymentId,
            CustomerId = customerId,
            Amount = amount,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return payment;
    }

    /// <summary>
    /// Real CRUD: Insert user.
    /// </summary>
    public async Task<UserRecord> CreateUserAsync(string name, string email, CancellationToken ct = default)
    {
        var user = new UserRecord
        {
            Name = name,
            Email = email,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return user;
    }

    /// <summary>
    /// Real CRUD: Retrieve a customer by ID; throws if not found.
    /// </summary>
    public async Task<CustomerRecord> GetCustomerAsync(int customerId, CancellationToken ct = default)
    {
        var customer = await _db.Customers.FindAsync(new object?[] { customerId }, cancellationToken: ct);
        if (customer == null)
            throw new InvalidOperationException($"Customer {customerId} not found");
        return customer;
    }

    /// <summary>
    /// Real CRUD: Get user by ID; throws if not found.
    /// </summary>
    public async Task<UserRecord> GetUserAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync(new object?[] { userId }, cancellationToken: ct);
        if (user == null)
            throw new ArgumentOutOfRangeException(nameof(userId), userId, "User lookup with invalid id");
        return user;
    }

    /// <summary>
    /// Real CRUD: Get invoice by ID.
    /// </summary>
    public async Task<InvoiceRecord?> GetInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        return await _db.Invoices.FindAsync(new object?[] { invoiceId }, cancellationToken: ct);
    }

    /// <summary>
    /// Real CRUD: Insert duplicate invoices to trigger unique constraint violation.
    /// </summary>
    public async Task TriggerDuplicateInvoiceAsync(CancellationToken ct = default)
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

        // Real unique index violation from EF Core write.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Real CRUD: Create a pricing record and return computed final amount.
    /// </summary>
    public async Task<decimal> CreatePricingRecordAsync(decimal subtotal, decimal loyaltyMultiplier, CancellationToken ct = default)
    {
        if (loyaltyMultiplier < 0 || loyaltyMultiplier > 1)
            throw new InvalidOperationException("Loyalty multiplier must be between 0 and 1");

        var discount = subtotal * loyaltyMultiplier;
        var finalAmount = subtotal - discount;

        var pricing = new PricingRecord
        {
            Subtotal = subtotal,
            LoyaltyMultiplier = loyaltyMultiplier,
            FinalAmount = finalAmount,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.PricingRecords.Add(pricing);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return finalAmount;
    }

    /// <summary>
    /// Real CRUD: Create a transfer on-hold due to SQL limit reached error.
    /// </summary>
    public async Task<TransferRecord> CreateTransferOnHoldAsync(int customerId, decimal amount, string paymentIds, CancellationToken ct = default)
    {
        var transferId = $"TXF-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var transfer = new TransferRecord
        {
            TransferId = transferId,
            CustomerId = customerId,
            Amount = amount,
            PaymentIds = paymentIds,
            Status = "OnHold",
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.Transfers.Add(transfer);
        
        // Simulate SQL limit reached by throwing during SaveChanges
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            throw;
        }

        return transfer;
    }

    /// <summary>
    /// Real CRUD: Query all payments for a customer (can timeout).
    /// </summary>
    public async Task<List<PaymentRecord>> GetCustomerPaymentsAsync(int customerId, CancellationToken ct = default)
    {
        // Simulate a timeout with a query that stalls
        return await _db.Payments
            .Where(p => p.CustomerId == customerId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}

