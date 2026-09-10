namespace InquiryService.Api.Options;

public sealed class ProviderOptions
{
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new();
}

public sealed class ProviderConfig
{
    public int Priority { get; set; }
    public int TimeoutSeconds { get; set; } = 5;
    public string Scenario { get; set; } = "Success";
    public int WorkMilliseconds { get; set; } = 50;
    public int LongDelaySeconds { get; set; } = 15;
}

public sealed class CacheOptions
{
    public int ResultTtlSeconds { get; set; } = 300;
}