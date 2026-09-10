using System.Diagnostics;
using System.Text.Json;
using InquiryService.Api.Domain;
using InquiryService.Api.Options;

namespace InquiryService.Api.Providers;

/// <summary>
/// A fake provider that stands in for a real external service. Its behaviour (success, business
/// error, technical failure or timeout) is driven by FakeProviderRuntime, which is seeded from
/// configuration and can be flipped at runtime for demonstrations and tests.
/// </summary>
public sealed class FakeProvider : IInquiryProvider
{
    private readonly FakeProviderRuntime _runtime;
    private readonly ProviderConfig _config;
    private readonly ILogger<FakeProvider> _logger;

    public FakeProvider(string name, FakeProviderRuntime runtime, ProviderConfig config, ILogger<FakeProvider> logger)
    {
        Name = name;
        _runtime = runtime;
        _config = config;
        _logger = logger;
    }

    public string Name { get; }
    public int Priority => _config.Priority;
    public TimeSpan Timeout => TimeSpan.FromSeconds(_config.TimeoutSeconds);

    public async Task<ProviderResult> GetResultAsync(InquiryRequestDto request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        ct.ThrowIfCancellationRequested();

        var scenario = _runtime.GetScenario(Name, _config);
        if (scenario == "Timeout")
        {
            _logger.LogWarning("[{Provider}] simulating a slow response ({Seconds}s, longer than my {Timeout}s timeout)", Name, _config.LongDelaySeconds, _config.TimeoutSeconds);
            await Task.Delay(TimeSpan.FromSeconds(_config.LongDelaySeconds), ct);
        }
        else
        {
            await Task.Delay(TimeSpan.FromMilliseconds(_config.WorkMilliseconds), ct);
        }

        switch (scenario)
        {
            case "BusinessError":
                _logger.LogInformation("[{Provider}] returning a business error response", Name);
                return new ProviderResult.BusinessResponse(
                    JsonSerializer.Serialize(new
                    {
                        inquiryRef = request.Reference,
                        customerId = request.CustomerId,
                        productId = request.ProductId,
                        provider = Name,
                        status = "error",
                        code = "INQUIRY_NOT_FOUND",
                        message = "No record matches the provided criteria."
                    }),
                    IsSuccess: false,
                    "INQUIRY_NOT_FOUND",
                    "No record matches the provided criteria.",
                    sw.Elapsed);

            case "TechnicalFailure":
                _logger.LogWarning("[{Provider}] simulating a connection failure", Name);
                throw new HttpRequestException($"Simulated connection failure from {Name}");

            case "Timeout":
                // Unreachable in practice: the caller cancels our token once the timeout fires.
                return new ProviderResult.BusinessResponse("{}", true, null, null, sw.Elapsed);

            default:
                _logger.LogInformation("[{Provider}] returning an approved result in {ElapsedMs}ms", Name, sw.ElapsedMilliseconds);
                return new ProviderResult.BusinessResponse(
                    JsonSerializer.Serialize(new
                    {
                        inquiryRef = request.Reference,
                        customerId = request.CustomerId,
                        productId = request.ProductId,
                        provider = Name,
                        status = "approved",
                        message = $"Processed by {Name}"
                    }),
                    IsSuccess: true,
                    null,
                    null,
                    sw.Elapsed);
        }
    }
}