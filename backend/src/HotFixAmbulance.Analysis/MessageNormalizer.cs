using System.Text.RegularExpressions;

namespace HotFixAmbulance.Analysis;

/// <summary>
/// Implements the normalization rules from the <c>serilog-mapping</c> skill so that two log messages that
/// differ only by ids/numbers/quoted literals hash to the same grouping key.
/// </summary>
public static partial class MessageNormalizer
{
    [GeneratedRegex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"""[^""]*""")]
    private static partial Regex QuotedRegex();

    [GeneratedRegex(@"\b\d{4,}\b")]
    private static partial Regex LongNumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var working = message.ToLowerInvariant();
        working = GuidRegex().Replace(working, "<id>");
        working = QuotedRegex().Replace(working, "<str>");
        working = LongNumberRegex().Replace(working, "<id>");
        working = WhitespaceRegex().Replace(working, " ").Trim();
        return working;
    }
}
