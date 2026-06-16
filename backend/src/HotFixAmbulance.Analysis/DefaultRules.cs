using HotFixAmbulance.Core;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// Built-in heuristic rules used by <see cref="HeuristicAnalyzer"/>. Order matters: more specific
/// patterns are listed first. Keep this list in sync with the <c>serilog-mapping</c> skill.
/// </summary>
internal static class DefaultRules
{
    public static IReadOnlyList<AnalysisRule> All { get; } =
    [
        new("NullReference",
            "Null reference — code dereferenced a null value; add a guard or initialize the dependency.",
            g => ContainsType(g, "NullReference")),

        new("Timeout",
            "Operation timeout — a downstream call exceeded its deadline; check dependency latency and retry policy.",
            g => ContainsType(g, "Timeout") || ContainsMessage(g, "timed out", "timeout")),

        new("Deadlock",
            "Database deadlock — review transaction scope, lock order, and missing indexes.",
            g => ContainsMessage(g, "deadlock")),

        new("Validation",
            "Validation failure — request payload did not satisfy the contract; align client and server schemas.",
            g => ContainsType(g, "Validation") || g.HttpStatus is 400 or 422),

        new("ServerError5xx",
            "Server-side 5xx — investigate the downstream call chain that handles this endpoint.",
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
