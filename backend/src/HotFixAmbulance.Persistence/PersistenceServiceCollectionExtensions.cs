using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HotFixAmbulance.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="HotFixDbContext"/> with SQLite using <c>Persistence:ConnectionString</c>
    /// (defaults to <c>Data Source=hotfix.db</c>) and the <see cref="TriageRunRepository"/>.
    /// </summary>
    public static IServiceCollection AddHotFixPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration["Persistence:ConnectionString"] ?? "Data Source=hotfix.db";
        services.AddDbContext<HotFixDbContext>(opt => opt.UseSqlite(connectionString));
        services.AddScoped<ITriageRunRepository, TriageRunRepository>();
        return services;
    }
}
