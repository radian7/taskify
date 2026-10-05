namespace Taskify.Security;

/// <summary>Server time as the database stores it.</summary>
public static class TimeProviderExtensions
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    /// <summary>
    /// Gets the current UTC time cut to whole microseconds. PostgreSQL stores <c>timestamptz</c> with microsecond
    /// precision while .NET times have 100-nanosecond ticks, so a response built from an uncut time would differ in its last
    /// digit from what the next read returns. Cutting the time first makes the two identical.
    /// </summary>
    /// <param name="time">The clock.</param>
    /// <returns>The current time in UTC with a whole number of microseconds.</returns>
    public static DateTimeOffset GetUtcNowMicroseconds(this TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        var now = time.GetUtcNow();
        return new DateTimeOffset(now.Ticks - (now.Ticks % TicksPerMicrosecond), TimeSpan.Zero);
    }
}
