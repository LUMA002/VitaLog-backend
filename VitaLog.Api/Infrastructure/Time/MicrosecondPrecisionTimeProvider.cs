namespace VitaLog.Api.Infrastructure.Time;

/// <summary>
/// Wraps an inner <see cref="TimeProvider"/> and truncates every timestamp to microsecond precision
/// (6 decimal places), matching both PostgreSQL <c>timestamptz</c> and Dart <c>DateTime</c>.
///
/// Without this, .NET's 100-nanosecond tick precision (7 decimal places) survives in memory but
/// is silently truncated by Postgres on write. On the next read the values differ at the sub-microsecond
/// digit, breaking bit-exact ACK comparisons in LWW sync and in integration tests.
///
/// Registering this as the sole <see cref="TimeProvider"/> singleton ensures every layer — auth,
/// seeding, sync handler — produces timestamps that survive a Postgres round-trip unchanged.
/// </summary>
public sealed class MicrosecondPrecisionTimeProvider(TimeProvider inner) : TimeProvider
{
    // 1 microsecond = 10 ticks (1 tick = 100 nanoseconds)
    private const long TicksPerMicrosecond = 10;

    public override DateTimeOffset GetUtcNow()
    {
        var value = inner.GetUtcNow();
        var truncatedTicks = value.UtcTicks - (value.UtcTicks % TicksPerMicrosecond);
        return new DateTimeOffset(truncatedTicks, TimeSpan.Zero);
    }
}
