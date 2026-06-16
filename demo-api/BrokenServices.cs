namespace DemoApi;

/// <summary>
/// Domain models exposed through the broken endpoints. Kept intentionally simple so the
/// NullReferenceException stack frame points at <c>OrderProcessor.GetCustomerEmail</c> — a
/// realistic ".Email on a null Customer" production bug.
/// </summary>
public sealed record Customer(string Id, string Name, string Email);

public sealed record OrderRequest(string? CustomerId, decimal Amount);

/// <summary>
/// In-memory customer store. Returns <c>null</c> for unknown ids so callers must null-check.
/// The bug demonstrated by the demo is that <see cref="OrderProcessor.GetCustomerEmail"/> does NOT
/// null-check before dereferencing <c>Customer.Email</c>.
/// </summary>
public sealed class CustomerRepository
{
    private readonly Dictionary<string, Customer> _byId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["c-001"] = new("c-001", "Alice Active", "alice@example.com"),
        ["c-002"] = new("c-002", "Bob Buyer", "bob@example.com"),
    };

    public Customer? FindByCustomerId(string? customerId) =>
        string.IsNullOrWhiteSpace(customerId) || !_byId.TryGetValue(customerId, out var c)
            ? null
            : c;
}

/// <summary>
/// Order workflow that contains the planted NullReferenceException.
/// </summary>
public sealed class OrderProcessor
{
    private readonly CustomerRepository _customers;
    private readonly ILogger<OrderProcessor> _log;

    public OrderProcessor(CustomerRepository customers, ILogger<OrderProcessor> log)
    {
        _customers = customers;
        _log = log;
    }

    /// <summary>
    /// PLANTED BUG: dereferences <c>customer.Email</c> without a null-check. When
    /// <see cref="CustomerRepository.FindByCustomerId"/> returns <c>null</c> for an unknown id
    /// (the common case in the demo traffic), this throws <see cref="NullReferenceException"/>
    /// with the property name <c>Email</c> in the stack frame.
    /// </summary>
    public string GetCustomerEmail(string? customerId)
    {
        var customer = _customers.FindByCustomerId(customerId);
        // ↓ this line is the bug: no null guard on `customer`.
        return customer.Email;
    }

    public Guid PlaceOrder(OrderRequest req)
    {
        var notifyAddress = GetCustomerEmail(req.CustomerId);
        _log.LogInformation("Order placed for {CustomerId}, will notify {Email}", req.CustomerId, notifyAddress);
        return Guid.NewGuid();
    }
}

/// <summary>
/// Simulated upstream payment provider whose call deadline is intentionally far below its p99.
/// </summary>
public sealed class PaymentGateway
{
    private readonly ILogger<PaymentGateway> _log;

    public PaymentGateway(ILogger<PaymentGateway> log) => _log = log;

    public async Task<decimal> AuthorizeAsync(string paymentId, CancellationToken ct)
    {
        // The provider's p99 is ~2s but we only give it 50ms -> guaranteed TaskCanceledException.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
        _log.LogInformation("Authorized {PaymentId}", paymentId);
        return 42m;
    }
}
