using FluentAssertions;
using HotFixAmbulance.Core;
using HotFixAmbulance.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HotFixAmbulance.UnitTests.Persistence;

public sealed class TriageRunRepositoryTests : IAsyncLifetime, IDisposable
{
    private HotFixDbContext _db = default!;
    private TriageRunRepository _sut = default!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<HotFixDbContext>()
            .UseInMemoryDatabase($"hfa-{Guid.NewGuid():N}")
            .Options;
        _db = new HotFixDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _sut = new TriageRunRepository(_db);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    public void Dispose() => _db.Dispose();

    private static TriageRun MakeRun(string api = "checkout-api", DateTimeOffset? at = null, string? analyzedBy = null) => new()
    {
        Id = Guid.NewGuid(),
        ApiName = api,
        RequestedAtUtc = at ?? DateTimeOffset.UtcNow,
        Lookback = TimeSpan.FromHours(24),
        TotalLogs = 1,
        GroupCount = 1,
        ErrorGroupsJson = "[]",
        AnalyzedBy = analyzedBy,
    };

    [Fact]
    public async Task AddAsync_persists_a_run()
    {
        var added = await _sut.AddAsync(MakeRun());
        added.Id.Should().NotBe(Guid.Empty);
        (await _db.TriageRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task GetLatestAsync_returns_newest_run_for_api()
    {
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await _sut.AddAsync(MakeRun(at: t));
        await _sut.AddAsync(MakeRun(at: t.AddHours(1)));
        await _sut.AddAsync(MakeRun(api: "orders", at: t.AddHours(2)));

        var latest = await _sut.GetLatestAsync("checkout-api");

        latest.Should().NotBeNull();
        latest!.RequestedAtUtc.Should().Be(t.AddHours(1));
    }

    [Fact]
    public async Task ListAsync_returns_descending_capped_results()
    {
        for (var i = 0; i < 5; i++)
        {
            await _sut.AddAsync(MakeRun(at: DateTimeOffset.UtcNow.AddMinutes(-i)));
        }

        var list = await _sut.ListAsync("checkout-api", take: 3);

        list.Should().HaveCount(3);
        list.Should().BeInDescendingOrder(r => r.RequestedAtUtc);
    }

    [Fact]
    public async Task ListAsync_throws_on_zero_take()
    {
        Func<Task> act = () => _sut.ListAsync("x", take: 0);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_run_when_present()
    {
        var run = await _sut.AddAsync(MakeRun());

        var found = await _sut.GetByIdAsync(run.Id);

        found.Should().NotBeNull();
        found!.Id.Should().Be(run.Id);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_unknown()
    {
        var found = await _sut.GetByIdAsync(Guid.NewGuid());
        found.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_persists_FromUtc_and_ToUtc_when_provided()
    {
        var from = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 3, 1, 14, 0, 0, TimeSpan.Zero);
        var run = new TriageRun
        {
            Id = Guid.NewGuid(),
            ApiName = "checkout-api",
            RequestedAtUtc = DateTimeOffset.UtcNow,
            Lookback = TimeSpan.FromHours(4),
            TotalLogs = 1,
            GroupCount = 1,
            ErrorGroupsJson = "[]",
            FromUtc = from,
            ToUtc = to,
        };

        var added = await _sut.AddAsync(run);
        var fetched = await _sut.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.FromUtc.Should().Be(from);
        fetched.ToUtc.Should().Be(to);
    }

    [Fact]
    public async Task AddAsync_allows_null_FromUtc_and_ToUtc_for_back_compat()
    {
        var added = await _sut.AddAsync(MakeRun());
        var fetched = await _sut.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.FromUtc.Should().BeNull();
        fetched.ToUtc.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_persists_AnalyzedBy_and_round_trips_it()
    {
        var added = await _sut.AddAsync(MakeRun(analyzedBy: "Llm"));
        var fetched = await _sut.GetByIdAsync(added.Id);

        fetched.Should().NotBeNull();
        fetched!.AnalyzedBy.Should().Be("Llm");
    }

    [Fact]
    public async Task AddAsync_allows_null_AnalyzedBy_for_back_compat()
    {
        var fetched = await _sut.GetByIdAsync((await _sut.AddAsync(MakeRun())).Id);

        fetched.Should().NotBeNull();
        fetched!.AnalyzedBy.Should().BeNull();
    }
}
