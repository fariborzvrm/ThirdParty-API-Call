using InquiryService.Api.Domain;
using InquiryService.Api.Providers;
using InquiryService.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace InquiryService.Tests;

public sealed class FailoverRunnerTests
{
    private static readonly InquiryRequestDto Request = new() { CustomerId = "C1", ProductId = "P1", Reference = "R1" };

    private sealed class StubProvider : IInquiryProvider
    {
        private readonly Func<CancellationToken, Task<ProviderResult>> _handler;

        public StubProvider(
            string name,
            int priority,
            Func<CancellationToken, Task<ProviderResult>> handler,
            TimeSpan? timeout = null)
        {
            Name = name;
            Priority = priority;
            _handler = handler;
            Timeout = timeout ?? TimeSpan.FromSeconds(2);
        }

        public string Name { get; }
        public int Priority { get; }
        public TimeSpan Timeout { get; }
        public int CallCount { get; private set; }

        public Task<ProviderResult> GetResultAsync(InquiryRequestDto request, CancellationToken ct)
        {
            CallCount++;
            return _handler(ct);
        }
    }

    private static FailoverRunner Runner(params IInquiryProvider[] providers)
        => new(providers, NullLogger<FailoverRunner>.Instance);

    private static ProviderResult Ok(string tag) =>
        new ProviderResult.BusinessResponse($"{{\"tag\":\"{tag}\"}}", true, null, null, TimeSpan.FromMilliseconds(10));

    private static ProviderResult BusinessError() =>
        new ProviderResult.BusinessResponse("{}", false, "INQUIRY_NOT_FOUND", "no record", TimeSpan.FromMilliseconds(10));

    private static ProviderResult TechnicalFailure(string message = "boom") =>
        new ProviderResult.TechnicalFailure(message, TimeSpan.FromMilliseconds(10));

    private static StubProvider SlowProvider(string name, int priority, TimeSpan timeout) =>
        new(name, priority, async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Ok(name);
        }, timeout);

    [Fact]
    public async Task First_available_provider_wins()
    {
        var first = new StubProvider("first", 1, _ => Task.FromResult(Ok("first")));
        var second = new StubProvider("second", 2, _ => Task.FromResult(Ok("second")));

        var result = await Runner(first, second).RunAsync(Request, CancellationToken.None);

        Assert.False(result.AllFailed);
        Assert.Equal("first", result.ServingProvider);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task Providers_are_called_in_priority_order()
    {
        var lowPriority = new StubProvider("secondary", 10, _ => Task.FromResult(Ok("secondary")));
        var highPriority = new StubProvider("primary", 1, _ => Task.FromResult(Ok("primary")));

        var result = await Runner(lowPriority, highPriority).RunAsync(Request, CancellationToken.None);

        Assert.Equal("primary", result.ServingProvider);
    }

    [Fact]
    public async Task Falls_back_to_next_provider_on_technical_failure()
    {
        var failing = new StubProvider("failing", 1, _ => Task.FromException<ProviderResult>(new HttpRequestException("connection refused")));
        var healthy = new StubProvider("healthy", 2, _ => Task.FromResult(Ok("healthy")));

        var result = await Runner(failing, healthy).RunAsync(Request, CancellationToken.None);

        Assert.False(result.AllFailed);
        Assert.Equal("healthy", result.ServingProvider);
        Assert.Equal(1, failing.CallCount);
        Assert.Equal(1, healthy.CallCount);

        var attempt = Assert.Single(result.Attempts, a => a.ProviderName == "failing");
        Assert.Equal("TechnicalFailure", attempt.Outcome);
        Assert.Equal("connection refused", attempt.OutcomeDetail);
    }

    [Fact]
    public async Task Does_not_fail_over_on_business_error()
    {
        var businessError = new StubProvider("busy", 1, _ => Task.FromResult(BusinessError()));
        var healthy = new StubProvider("healthy", 2, _ => Task.FromResult(Ok("healthy")));

        var result = await Runner(businessError, healthy).RunAsync(Request, CancellationToken.None);

        Assert.False(result.AllFailed);
        Assert.True(result.IsBusinessError);
        Assert.Equal("busy", result.ServingProvider);
        Assert.Equal("INQUIRY_NOT_FOUND", result.BusinessErrorCode);
        Assert.Equal(0, healthy.CallCount);
    }

    [Fact]
    public async Task Falls_back_on_timeout()
    {
        var slow = SlowProvider("slow", 1, TimeSpan.FromMilliseconds(100));
        var healthy = new StubProvider("healthy", 2, _ => Task.FromResult(Ok("healthy")));

        var result = await Runner(slow, healthy).RunAsync(Request, CancellationToken.None);

        Assert.False(result.AllFailed);
        Assert.Equal("healthy", result.ServingProvider);

        var attempt = Assert.Single(result.Attempts, a => a.ProviderName == "slow");
        Assert.Equal("TechnicalFailure", attempt.Outcome);
        Assert.Contains("timed out", attempt.OutcomeDetail);
    }

    [Fact]
    public async Task All_technical_failures_are_reported()
    {
        var first = new StubProvider("a", 1, _ => Task.FromException<ProviderResult>(new Exception("down")));
        var second = SlowProvider("b", 2, TimeSpan.FromMilliseconds(100));

        var result = await Runner(first, second).RunAsync(Request, CancellationToken.None);

        Assert.True(result.AllFailed);
        Assert.Null(result.FinalPayload);
        Assert.Null(result.ServingProvider);
        Assert.Equal(2, result.Attempts.Count);
        Assert.All(result.Attempts, a => Assert.Equal("TechnicalFailure", a.Outcome));
        Assert.Contains("timed out", result.FailureReason);
    }

    [Fact]
    public async Task Attempt_history_records_order_and_timing()
    {
        var failing = new StubProvider("a", 1, _ => Task.FromException<ProviderResult>(new Exception("down")));
        var healthy = new StubProvider("b", 2, _ => Task.FromResult(Ok("b")));

        var result = await Runner(failing, healthy).RunAsync(Request, CancellationToken.None);

        Assert.Equal(new[] { "a", "b" }, result.Attempts.Select(a => a.ProviderName));
        Assert.Equal(new[] { 1, 2 }, result.Attempts.Select(a => a.AttemptOrder));
        Assert.All(result.Attempts, a => Assert.True(a.ElapsedMs >= 0));
    }
}