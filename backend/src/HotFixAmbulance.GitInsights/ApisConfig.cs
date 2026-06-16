using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HotFixAmbulance.GitInsights;

/// <summary>
/// In-memory view of <c>apis.config.json</c>. Maps <c>apiName → <see cref="ApiRepoEntry"/></c>.
/// </summary>
public sealed class ApisConfig
{
    private readonly IReadOnlyDictionary<string, ApiRepoEntry> _entries;

    public ApisConfig(IReadOnlyDictionary<string, ApiRepoEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = new Dictionary<string, ApiRepoEntry>(entries, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> KnownApis => (IReadOnlyCollection<string>)_entries.Keys;

    public bool TryGet(string apiName, [NotNullWhen(true)] out ApiRepoEntry? entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        return _entries.TryGetValue(apiName, out entry);
    }

    public static ApisConfig LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"apis config not found: {path}", path);
        }

        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<ApisConfigDto>(stream, JsonOpts)
            ?? throw new InvalidDataException($"apis config is empty: {path}");

        var dict = new Dictionary<string, ApiRepoEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, raw) in dto.Apis)
        {
            if (raw.Url is null)
            {
                throw new InvalidDataException($"apis.{name}.url is required.");
            }
            dict[name] = new ApiRepoEntry(name, raw.Url, string.IsNullOrWhiteSpace(raw.Branch) ? "main" : raw.Branch!);
        }
        return new ApisConfig(dict);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed record ApisConfigDto(
        [property: JsonPropertyName("apis")] IReadOnlyDictionary<string, ApiRepoRawDto> Apis);

    private sealed record ApiRepoRawDto(
        [property: JsonPropertyName("url")] Uri? Url,
        [property: JsonPropertyName("branch")] string? Branch);
}
