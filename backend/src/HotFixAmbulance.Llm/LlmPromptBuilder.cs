using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using HotFixAmbulance.Core;

namespace HotFixAmbulance.Llm;

/// <summary>
/// Turns an <see cref="ErrorGroup"/> (plus optional git-history evidence) into the system + user
/// prompt pair sent to a local LLM. The system prompt pins a strict JSON contract so the response
/// can be parsed into the two AI columns; the user prompt packs the concrete facts of the group and
/// the git evidence verbatim so the model's answer is grounded rather than invented.
/// </summary>
public sealed class LlmPromptBuilder
{
    private const string SystemPrompt =
        """
        You are a senior SRE assistant triaging production errors for a .NET Web API.
        For the single error group described by the user, write a short, actionable triage note.

        Reply with ONLY a JSON object — no markdown fences, no prose around it — with exactly these
        two string keys:
          "suggestion": one or two sentences explaining WHAT the error means and its likely root cause.
          "howToFix":   a concrete remediation (HOW to fix). When git-history evidence is provided,
                        ground your answer in it: reference the offending file/line/symbol and the
                        relevant commits instead of guessing.

        Keep each value under ~60 words. Do not invent file paths, commits, endpoints, or APIs that are
        not supported by the facts you are given.
        """;

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Registered and injected as a stateless DI service; kept instance-based for composition and future template configuration.")]
    public LlmPrompt Build(ErrorGroup group, string? gitEvidence)
    {
        ArgumentNullException.ThrowIfNull(group);

        var sb = new StringBuilder();
        sb.AppendLine("Error group facts:");
        sb.Append("- Severity: ").AppendLine(group.Severity.ToString());
        sb.Append("- Occurrences: ").AppendLine(group.Count.ToString(CultureInfo.InvariantCulture));
        sb.Append("- First seen (UTC): ").AppendLine(group.FirstSeenUtc.ToString("u", CultureInfo.InvariantCulture));
        sb.Append("- Last seen (UTC): ").AppendLine(group.LastSeenUtc.ToString("u", CultureInfo.InvariantCulture));
        sb.Append("- Exception type: ").AppendLine(Or(group.ExceptionType, "(none)"));
        sb.Append("- Message: ").AppendLine(Or(group.Message, "(none)"));
        sb.Append("- Endpoint: ").AppendLine(Or(group.Endpoint, "(unknown)"));
        sb.Append("- HTTP status: ").AppendLine(group.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
        sb.Append("- Service version: ").AppendLine(Or(group.ServiceVersion, "(unknown)"));
        sb.Append("- Distinct correlation IDs: ").AppendLine(group.CorrelationIdCount.ToString(CultureInfo.InvariantCulture));
        sb.Append("- Stack symbol: ").AppendLine(Or(group.StackSymbol, "(unknown)"));
        sb.Append("- Stack location: ").AppendLine(FormatStackLocation(group));

        sb.AppendLine();
        sb.AppendLine("Git-history evidence (from origin/main):");
        sb.AppendLine(string.IsNullOrWhiteSpace(gitEvidence) ? "(none available)" : gitEvidence);

        return new LlmPrompt(SystemPrompt, sb.ToString().TrimEnd());
    }

    private static string FormatStackLocation(ErrorGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.StackFile))
        {
            return "(unknown)";
        }
        return group.StackLine is int line
            ? string.Create(CultureInfo.InvariantCulture, $"{group.StackFile}:{line}")
            : group.StackFile!;
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
