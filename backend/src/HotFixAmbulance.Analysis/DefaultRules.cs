using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// Built-in heuristic rules used by <see cref="HeuristicAnalyzer"/>. Order matters: more specific
/// patterns are listed first. Each rule provides BOTH a <c>Suggestion</c> (interpretation of WHAT
/// the error means) and a <c>HowToFix</c> (concrete remediation steps), so the UI's two AI columns
/// always show distinct-but-related text. The git-insights layer may later override <c>HowToFix</c>
/// with a real commit from <c>origin/main</c>. Keep this list in sync with the
/// <c>serilog-mapping</c> skill.
/// </summary>
internal static class DefaultRules
{
    public static IReadOnlyList<AnalysisRule> All { get; } =
    [
        new("NullReference",
            Suggestion: "Null reference — code dereferenced a null value before checking it.",
            HowToFix: "Add a null/`ArgumentNullException.ThrowIfNull` guard at the boundary; "
                + "initialize the dependency in DI, or use the null-conditional `?.` operator on the offending member access.",
            g => ContainsType(g, "NullReference")),

        new("Timeout",
            Suggestion: "Operation timeout — a downstream call exceeded its deadline.",
            HowToFix: "Increase `HttpClient.Timeout` (or the SQL command timeout) for this call, "
                + "add a Polly retry-with-jitter policy, and verify the dependency's p99 latency in the dashboards.",
            g => ContainsType(g, "Timeout") || ContainsMessage(g, "timed out", "timeout")),

        new("Deadlock",
            Suggestion: "Database deadlock — two transactions blocked on opposite lock orders.",
            HowToFix: "Order writes consistently across transactions, shorten the transaction scope, "
                + "and check whether a missing covering index on the hot table is forcing a table lock.",
            g => ContainsMessage(g, "deadlock")),

        new("Validation",
            Suggestion: "Validation failure — the request payload did not satisfy the contract.",
            HowToFix: "Re-generate the client DTOs from the OpenAPI spec, and align the FluentValidation "
                + "rules with the latest schema; return a `ProblemDetails` body so the caller can self-correct.",
            g => ContainsType(g, "Validation") || g.HttpStatus is 400 or 422),

        new("ServerError5xx",
            Suggestion: "Server-side 5xx — an unhandled exception bubbled out of the endpoint.",
            HowToFix: "Wrap the failing call in a typed exception handler, surface a correlation id in the "
                + "response, and add a Serilog enricher for the downstream call chain (DbCommand / HttpClient).",
            g => g.HttpStatus is >= 500 and <= 599),
    ];

    private static bool ContainsType(ErrorGroup g, string fragment) =>
        g.ExceptionType is not null
        && g.ExceptionType.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsMessage(ErrorGroup g, params string[] fragments)
    {
        if (string.IsNullOrEmpty(g.Message))
        {
            return false;
        }

        foreach (var f in fragments)
        {
            if (g.Message.Contains(f, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
