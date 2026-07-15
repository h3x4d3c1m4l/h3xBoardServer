namespace H3xBoardServer.Tests;

/// <summary>A manually advanced clock for TTL/presence expiry tests.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public FakeTimeProvider() : this(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
