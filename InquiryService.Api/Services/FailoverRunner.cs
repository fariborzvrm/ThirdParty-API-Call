using System.Diagnostics;
using InquiryService.Api.Domain;
using InquiryService.Api.Providers;

namespace InquiryService.Api.Services;

public sealed record FailoverRunnerResult(
    bool AllFailed,
    string? FinalPayload,
    string? ServingProvider,
    bool IsBusinessError,
    string? BusinessErrorCode,
    string? BusinessErrorMessage,
    string? FailureReason,
    IReadOnlyList<ProviderAttemptDto> Attempts);

/// <summary>
/// Runs the provider pipeline: providers are tried in priority order and a technical failure
/// (timeout, exception) falls through to the next one. A valid business response — including one
/// that carries a business-level error — is final and stops the loop.
/// </summary>
public sealed class FailoverRunner
{
    private readonly IReadOnlyList<IInquiryProvider> _providers;
    private readonly ILogger<FailoverRunner> _logger;

    public FailoverRunner(IEnumerable<IInquiryProvider> providers, ILogger<FailoverRunner> logger)
    {
        _providers = providers.OrderBy(p => p.Priority).ThenBy(p => p.Name).ToList();
        _logger = logger;
    }

    public async Task<FailoverRunnerResult> RunAsync(InquiryRequestDto request, CancellationToken ct)
    {
        var attempts = new List<ProviderAttemptDto>(_providers.Count);
        string? failureReason = null;

        for (var order = 0; order < _providers.Count; order++)
        {
            var provider = _providers[order];
            var sw = Stopwatch.StartNew();
            ProviderResult outcome;

            try
            {
                using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                providerCts.CancelAfter(provider.Timeout);
                outcome = await provider.GetResultAsync(request, providerCts.Token).WaitAsync(provider.Timeout, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                outcome = new ProviderResult.TechnicalFailure($"timed out after {provider.Timeout.TotalSeconds}s", provider.Timeout);
            }
            catch (TimeoutException)
            {
                outcome = new ProviderResult.TechnicalFailure($"timed out after {provider.Timeout.TotalSeconds}s", provider.Timeout);
            }
            catch (Exception ex)
            {
                outcome = new ProviderResult.TechnicalFailure(ex.Message, sw.Elapsed);
            }
            finally
            {
                sw.Stop();
            }

            attempts.Add(new ProviderAttemptDto(provider.Name, order + 1, OutcomeOf(outcome), DetailOf(outcome), (long)outcome.Elapsed.TotalMilliseconds));

            if (outcome is ProviderResult.BusinessResponse br)
            {
                _logger.LogInformation(
                    "Provider {Provider} answered with a business response (error={IsBusinessError}) in {ElapsedMs}ms; failover stops",
                    provider.Name, !br.IsSuccess, outcome.Elapsed.TotalMilliseconds);
                return new FailoverRunnerResult(
                    false,
                    br.Payload,
                    provider.Name,
                    !br.IsSuccess,
                    br.ErrorCode,
                    br.ErrorMessage,
                    null,
                    attempts);
            }

            failureReason = $"Provider {provider.Name} failed: {((ProviderResult.TechnicalFailure)outcome).Error}";
            _logger.LogWarning("Provider {Provider} failed technically ({Error}); trying next provider", provider.Name, ((ProviderResult.TechnicalFailure)outcome).Error);
        }

        _logger.LogWarning("All {Count} providers failed; last reason: {Failure}", _providers.Count, failureReason);
        return new FailoverRunnerResult(true, null, null, false, null, null, failureReason, attempts);
    }

    private static string OutcomeOf(ProviderResult result) => result switch
    {
        ProviderResult.BusinessResponse { IsSuccess: true } => "Success",
        ProviderResult.BusinessResponse => "BusinessError",
        _ => "TechnicalFailure"
    };

    private static string? DetailOf(ProviderResult result) => result switch
    {
        ProviderResult.BusinessResponse br => br.ErrorMessage,
        ProviderResult.TechnicalFailure tf => tf.Error,
        _ => null
    };
}