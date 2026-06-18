namespace HotFixAmbulance.Core;

/// <summary>
/// Absolute UTC time window for a log analysis run. Use <see cref="Relative"/> to build a
/// "last N" window anchored at <c>now</c>, or <see cref="Absolute"/> for an explicit range
/// picked by the user. Construction enforces <c>From &lt; To</c> and a configurable maximum span
/// so we never accidentally ask Elasticsearch for years of logs.
/// </summary>
public sealed record TimeWindow
{
    /// <summary>Hard upper bound on the window span enforced by <see cref="Validate"/>.</summary>
    public static readonly TimeSpan DefaultMaxSpan = TimeSpan.FromDays(30);

    /// <summary>Inclusive start of the window, in UTC.</summary>
    public DateTimeOffset FromUtc { get; }

    /// <summary>Inclusive end of the window, in UTC.</summary>
    public DateTimeOffset ToUtc { get; }

    /// <summary>The duration of the window (<c>ToUtc - FromUtc</c>).</summary>
    public TimeSpan Duration => ToUtc - FromUtc;

    private TimeWindow(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        FromUtc = fromUtc.ToUniversalTime();
        ToUtc = toUtc.ToUniversalTime();
    }

    /// <summary>
    /// Build a window ending at <paramref name="now"/> and starting <paramref name="lookback"/>
    /// earlier. Equivalent to the old <c>From = now - lookback, To = now</c> computation.
    /// </summary>
    public static TimeWindow Relative(DateTimeOffset now, TimeSpan lookback, TimeSpan? maxSpan = null)
    {
        if (lookback <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lookback), lookback, "Lookback must be positive.");
        }

        var window = new TimeWindow(now - lookback, now);
        window.Validate(maxSpan ?? DefaultMaxSpan);
        return window;
    }

    /// <summary>
    /// Build a window from an explicit <paramref name="fromUtc"/>/<paramref name="toUtc"/>
    /// pair. Both timestamps are normalised to UTC.
    /// </summary>
    public static TimeWindow Absolute(DateTimeOffset fromUtc, DateTimeOffset toUtc, TimeSpan? maxSpan = null)
    {
        var window = new TimeWindow(fromUtc, toUtc);
        window.Validate(maxSpan ?? DefaultMaxSpan);
        return window;
    }

    private void Validate(TimeSpan maxSpan)
    {
        if (FromUtc >= ToUtc)
        {
            throw new ArgumentException(
                $"Time window must have FromUtc < ToUtc (got FromUtc={FromUtc:o}, ToUtc={ToUtc:o}).");
        }

        if (maxSpan <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSpan), maxSpan, "Max span must be positive.");
        }

        if (Duration > maxSpan)
        {
            throw new ArgumentException(
                $"Time window span {Duration} exceeds the maximum allowed {maxSpan}.");
        }
    }
}
