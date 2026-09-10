using InquiryService.Api.Domain;

namespace InquiryService.Api.Providers;

public interface IInquiryProvider
{
    string Name { get; }
    int Priority { get; }
    TimeSpan Timeout { get; }
    Task<ProviderResult> GetResultAsync(InquiryRequestDto request, CancellationToken ct);
}