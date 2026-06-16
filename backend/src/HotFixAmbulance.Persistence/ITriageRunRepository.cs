namespace HotFixAmbulance.Persistence;

public interface ITriageRunRepository
{
    Task<TriageRun> AddAsync(TriageRun run, CancellationToken cancellationToken = default);

    Task<TriageRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TriageRun?> GetLatestAsync(string apiName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TriageRun>> ListAsync(string apiName, int take = 20, CancellationToken cancellationToken = default);
}
