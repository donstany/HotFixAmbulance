namespace HotFixAmbulance.GitInsights;

/// <summary>
/// Snapshot of the source code around an offending stack-trace line, read from the tip of the
/// tracked branch (e.g. <c>origin/main</c>), plus the most-precise <see cref="CommitSummary"/>
/// obtained via <c>git blame</c> for that exact line.
/// </summary>
/// <param name="ResolvedPath">Repository-relative path to the file (forward slashes).</param>
/// <param name="StartLine">1-based line number of the first line in <see cref="Lines"/>.</param>
/// <param name="OffendingLine">1-based line number of the line that threw.</param>
/// <param name="Lines">The slice of file lines around <see cref="OffendingLine"/> (inclusive both ends).</param>
/// <param name="Blame">Blame attribution for <see cref="OffendingLine"/>. Null when blame is unavailable.</param>
public sealed record FileLineContext(
    string ResolvedPath,
    int StartLine,
    int OffendingLine,
    IReadOnlyList<string> Lines,
    CommitSummary? Blame);
