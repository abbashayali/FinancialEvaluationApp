using Microsoft.Extensions.Caching.Memory;

public interface ILoginThrottle
{
    bool IsLocked(string username, string ip, out TimeSpan? retryAfter);
    void RegisterFail(string username, string ip);
    void RegisterSuccess(string username, string ip);
}

public class MemoryLoginThrottle : ILoginThrottle
{
    private readonly IMemoryCache _cache;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(10);

    public MemoryLoginThrottle(IMemoryCache cache) => _cache = cache;

    private static string Key(string u, string ip) => $"login:{u}:{ip}".ToLowerInvariant();

    public bool IsLocked(string username, string ip, out TimeSpan? retryAfter)
    {
        var key = Key(username, ip);
        if (_cache.TryGetValue<(int fails, DateTimeOffset? lockedUntil)>(key, out var state)
            && state.lockedUntil.HasValue)
        {
            var remain = state.lockedUntil.Value - DateTimeOffset.UtcNow;
            if (remain > TimeSpan.Zero) { retryAfter = remain; return true; }
        }
        retryAfter = null;
        return false;
    }

    public void RegisterFail(string username, string ip)
    {
        var key = Key(username, ip);
        var now = DateTimeOffset.UtcNow;

        if (_cache.TryGetValue<(int fails, DateTimeOffset? lockedUntil)>(key, out var state))
        {
            state.fails++;
            if (state.fails >= MaxAttempts)
                state.lockedUntil = now.Add(Lockout);

            _cache.Set(key, state, Window);
        }
        else
        {
            _cache.Set(key, (1, (DateTimeOffset?)null), Window);
        }
    }

    public void RegisterSuccess(string username, string ip)
    {
        _cache.Remove(Key(username, ip));
    }
}
