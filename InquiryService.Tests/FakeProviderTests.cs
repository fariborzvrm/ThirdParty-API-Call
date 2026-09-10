using InquiryService.Api.Domain;
using InquiryService.Api.Options;
using InquiryService.Api.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace InquiryService.Tests;

public sealed class FakeProviderTests
{
    private static readonly InquiryRequestDto Request = new() { CustomerId = "C1", ProductId = "P1", Reference = "R1" };

    private static (FakeProviderRuntime Runtime, FakeProvider Provider) Create(string scenario)
    {
        var runtime = new FakeProviderRuntime();
        var config = new ProviderConfig
        {
            Priority = 1,
            TimeoutSeconds = 2,
            Scenario = scenario,
            WorkMilliseconds = 1,
            LongDelaySeconds = 10
        };
        var provider = new FakeProvider("TestProvider", runtime, config, NullLogger<FakeProvider>.Instance);
        return (runtime, provider);
    }

    [Fact]
    public async Task Success_scenario_returns_a_business_success_response()
    {
        var (_, provider) = Create("Success");

        var result = await provider.GetResultAsync(Request, CancellationToken.None);

        var response = Assert.IsType<ProviderResult.BusinessResponse>(result);
        Assert.True(response.IsSuccess);
        Assert.Contains("TestProvider", response.Payload);
    }

    [Fact]
    public async Task BusinessError_scenario_returns_a_business_error_without_throwing()
    {
        var (_, provider) = Create("BusinessError");

        var result = await provider.GetResultAsync(Request, CancellationToken.None);

        var response = Assert.IsType<ProviderResult.BusinessResponse>(result);
        Assert.False(response.IsSuccess);
        Assert.Equal("INQUIRY_NOT_FOUND", response.ErrorCode);
        Assert.False(string.IsNullOrEmpty(response.ErrorMessage));
    }

    [Fact]
    public async Task TechnicalFailure_scenario_throws()
    {
        var (_, provider) = Create("TechnicalFailure");

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetResultAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_scenario_aborts_when_its_token_cancels()
    {
        var (_, provider) = Create("Timeout");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetResultAsync(Request, cts.Token));
    }

    [Fact]
    public async Task Runtime_override_changes_behavior_without_reconfiguring()
    {
        var (runtime, provider) = Create("Success");

        runtime.SetScenario("TestProvider", "TechnicalFailure");

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetResultAsync(Request, CancellationToken.None));
    }
}