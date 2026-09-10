namespace InquiryService.Api.Providers;

/// <summary>
/// Result of a single provider call. A BusinessResponse means the provider answered with a valid
/// response (success or business-level error) and is therefore final. A TechnicalFailure means the
/// call itself broke down (timeout, connection error, exception) and is eligible for failover.
/// </summary>
public abstract record ProviderResult(TimeSpan Elapsed)
{
    public sealed record BusinessResponse(
        string Payload,
        bool IsSuccess,
        string? ErrorCode,
        string? ErrorMessage,
        TimeSpan Elapsed) : ProviderResult(Elapsed);

    public sealed record TechnicalFailure(
        string Error,
        TimeSpan Elapsed) : ProviderResult(Elapsed);
}