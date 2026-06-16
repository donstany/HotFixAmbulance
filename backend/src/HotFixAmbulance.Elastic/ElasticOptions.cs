using System.ComponentModel.DataAnnotations;

namespace HotFixAmbulance.Elastic;

/// <summary>
/// Bound from <c>Elastic:*</c> configuration (user-secrets or .env). See <c>.env.example</c>.
/// </summary>
public sealed class ElasticOptions
{
    /// <summary>Configuration section. Combined with the host's <c>HFA_</c> prefix this matches <c>HFA_ELASTIC__*</c> env vars.</summary>
    public const string SectionName = "Elastic";

    [Required]
    public Uri Uri { get; set; } = default!;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>API key (alternative to username/password). Format: <c>id:api_key</c> base64-encoded.</summary>
    public string? ApiKey { get; set; }

    [Required]
    public string IndexPattern { get; set; } = "logs-*";

    /// <summary>Max documents per <c>search_after</c> page. Defaults to 1000.</summary>
    [Range(1, 10_000)]
    public int PageSize { get; set; } = 1000;

    /// <summary>Maximum total documents to materialize per query. Defaults to 10 000.</summary>
    [Range(1, 100_000)]
    public int MaxDocuments { get; set; } = 10_000;

    /// <summary>How many times Polly should retry a transient failure.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;
}
