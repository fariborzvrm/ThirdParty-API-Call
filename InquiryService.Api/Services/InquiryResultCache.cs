using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using InquiryService.Api.Domain;
using InquiryService.Api.Options;

namespace InquiryService.Api.Services;

public sealed class InquiryResultCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;
    private const string Prefix = "inquiry-result:";

    public InquiryResultCache(IMemoryCache cache, IOptions<CacheOptions> options)
    {
        _cache = cache;
        _ttl = TimeSpan.FromSeconds(options.Value.ResultTtlSeconds > 0 ? options.Value.ResultTtlSeconds : 300);
    }

    public bool TryGet(string key, out InquiryResultDto? result)
        => _cache.TryGetValue(Prefix + key, out result);

    public void Set(string key, InquiryResultDto result)
        => _cache.Set(Prefix + key, result, _ttl);
}