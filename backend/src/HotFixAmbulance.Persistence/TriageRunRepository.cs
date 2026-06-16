using Microsoft.EntityFrameworkCore;

namespace HotFixAmbulance.Persistence;

public sealed class TriageRunRepository : ITriageRunRepository
{
    private readonly HotFixDbContext _db;

    public TriageRunRepository(HotFixDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public async Task<TriageRun> AddAsync(TriageRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        await _db.TriageRuns.AddAsync(run, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return run;
    }

    public Task<TriageRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _db.TriageRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public Task<TriageRun?> GetLatestAsync(string apiName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        return _db.TriageRuns
            .AsNoTracking()
            .Where(r => r.ApiName == apiName)
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TriageRun>> ListAsync(string apiName, int take = 20, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        if (take <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "Must be positive.");
        }

        var list = await _db.TriageRuns
            .AsNoTracking()
            .Where(r => r.ApiName == apiName)
            .OrderByDescending(r => r.RequestedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return list;
    }
}
