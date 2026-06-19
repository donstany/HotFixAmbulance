using Microsoft.EntityFrameworkCore;

namespace HotFixAmbulance.Persistence;

/// <summary>
/// One-shot schema bootstrap for the SQLite store. Runs <c>EnsureCreatedAsync</c>
/// then applies any additive columns the live schema is missing. Idempotent.
/// </summary>
public static class HotFixDbContextSchema
{
    /// <summary>
    /// Ensures the <see cref="HotFixDbContext"/> schema exists and is upgraded to the current
    /// model. Safe to call on every startup. For relational providers, runs additive
    /// <c>ALTER TABLE</c> statements when columns added after the original <c>EnsureCreated</c>
    /// snapshot are not yet present (e.g. <c>FromUtc</c>/<c>ToUtc</c> added in Phase 12.C).
    /// </summary>
    public static async Task EnsureSchemaAsync(this HotFixDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        // EnsureCreated does NOT alter existing tables. For SQLite (the only relational
        // provider used in this project) we patch the schema by hand using PRAGMA + ALTER TABLE.
        // The InMemory provider used by integration tests is non-relational and skipped here.
        if (!db.Database.IsRelational())
        {
            return;
        }

        var existingColumns = await GetTriageRunColumnsAsync(db, cancellationToken).ConfigureAwait(false);

        if (!existingColumns.Contains("FromUtc"))
        {
            await db.Database
                .ExecuteSqlRawAsync("ALTER TABLE TriageRuns ADD COLUMN FromUtc TEXT NULL", cancellationToken)
                .ConfigureAwait(false);
        }
        if (!existingColumns.Contains("ToUtc"))
        {
            await db.Database
                .ExecuteSqlRawAsync("ALTER TABLE TriageRuns ADD COLUMN ToUtc TEXT NULL", cancellationToken)
                .ConfigureAwait(false);
        }
        if (!existingColumns.Contains("AnalyzedBy"))
        {
            await db.Database
                .ExecuteSqlRawAsync("ALTER TABLE TriageRuns ADD COLUMN AnalyzedBy TEXT NULL", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<HashSet<string>> GetTriageRunColumnsAsync(HotFixDbContext db, CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(TriageRuns);";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk
            existing.Add(reader.GetString(1));
        }
        return existing;
    }
}
