using System.Collections.Concurrent;

namespace H3xBoardServer.Services.Sharing;

/// <summary>
/// Per-IP token bucket for share-code lookups on the anonymous viewer endpoint — 10 attempts per
/// minute per IP makes brute-forcing 6-character codes (~900M combinations) impractical while
/// leaving legitimate retries comfortable. In-memory and per-instance by design: an attacker
/// spraying across instances still gets only N×10/min, which is noise at this keyspace.
/// Registered as a singleton.
/// </summary>
public class ShareCodeRateLimiter(TimeProvider? timeProvider = null)
{
    private const double Capacity = 10;
    private static readonly TimeSpan RefillWindow = TimeSpan.FromMinutes(1);  // Capacity tokens per window
    private static readonly TimeSpan IdleEviction = TimeSpan.FromMinutes(10);
    private const int SweepThreshold = 1000;

    private sealed class Bucket
    {
        public double Tokens = Capacity;
        public DateTimeOffset LastRefill;
    }

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    /// <summary>Takes one token for <paramref name="key"/> (an IP). False = rate limited.</summary>
    public bool TryAcquire(string key)
    {
        var now = _time.GetUtcNow();
        SweepIfNeeded(now);

        var bucket = _buckets.GetOrAdd(key, _ => new Bucket { LastRefill = now });
        lock (bucket)
        {
            var elapsed = now - bucket.LastRefill;
            if (elapsed > TimeSpan.Zero)
            {
                bucket.Tokens = Math.Min(Capacity, bucket.Tokens + elapsed / RefillWindow * Capacity);
                bucket.LastRefill = now;
            }

            if (bucket.Tokens < 1)
                return false;
            bucket.Tokens -= 1;
            return true;
        }
    }

    private void SweepIfNeeded(DateTimeOffset now)
    {
        if (_buckets.Count < SweepThreshold)
            return;

        foreach (var (key, bucket) in _buckets)
        {
            if (now - bucket.LastRefill > IdleEviction)
                _buckets.TryRemove(key, out _);
        }
    }
}
