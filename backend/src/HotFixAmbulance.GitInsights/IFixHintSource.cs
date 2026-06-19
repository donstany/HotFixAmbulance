using HotFixAmbulance.Core;

namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Produces the "How to fix" evidence string for an <see cref="ErrorGroup"/> by mining the API's
/// git history. Extracted so consumers (e.g. the analysis enrichers) can depend on the seam and be
/// unit-tested with a fake. Implemented by <see cref="FixHintBuilder"/>.
/// </summary>
public interface IFixHintSource
{
    /// <summary>
    /// Returns a formatted git-history hint for <paramref name="group"/>, or <c>null</c> when the
    /// API is unknown, the repo can't be reached, or no relevant commits/snippet are found.
    /// </summary>
    Task<string?> BuildAsync(string apiName, ErrorGroup group, CancellationToken cancellationToken = default);
}
