using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SKF.ProductAssistant.Functions.Services;

public interface ICacheService
{
    Task<string?> GetStringAsync(string key);
    Task SetStringAsync(string key, string value, TimeSpan ttl);
}

public class CacheService : ICacheService
{
    private readonly ILogger<CacheService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly ConnectionMultiplexer? _redis;
    private readonly IDatabase? _redisDb;

    public CacheService(ILogger<CacheService> logger, IMemoryCache memoryCache, IConfiguration config)
    {
        _logger = logger;
        _memoryCache = memoryCache;

        var connectionString = config["REDIS_CONNECTION"] ?? config["REDIS_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                _redis = ConnectionMultiplexer.Connect(connectionString);
                _redisDb = _redis.GetDatabase();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to Redis. Falling back to in-memory cache.");
            }
        }
    }

    public async Task<string?> GetStringAsync(string key)
    {
        if (_redisDb != null)
        {
            return await _redisDb.StringGetAsync(key);
        }

        if (_memoryCache.TryGetValue<string>(key, out var value))
        {
            return value;
        }
        return null;
    }

    public async Task SetStringAsync(string key, string value, TimeSpan ttl)
    {
        if (_redisDb != null)
        {
            await _redisDb.StringSetAsync(key, value, ttl);
            return;
        }

        _memoryCache.Set(key, value, ttl);
    }
}


