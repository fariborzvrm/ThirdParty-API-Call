using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InquiryService.Api.Domain;
using InquiryService.Api.Infrastructure;
using InquiryService.Api.Providers;
using Microsoft.EntityFrameworkCore;

namespace InquiryService.Api.Services;

/// <summary>
/// Orchestrates a single inquiry submission: cache lookup, duplicate/in-flight protection,
/// the provider failover pipeline, and persistence of the result and attempt history.
/// </summary>
public sealed class InquirySubmissionService
{
    private static readonly TimeSpan PeerWaitDeadline = TimeSpan.FromSeconds(15);

    private readonly InquiryDbContext _db;
    private readonly FailoverRunner _runner;
    private readonly InquiryResultCache _cache;
    private readonly KeyedLocks _locks;
    private readonly ILogger<InquirySubmissionService> _logger;

    public InquirySubmissionService(
        InquiryDbContext db,
        FailoverRunner runner,
        InquiryResultCache cache,
        KeyedLocks locks,
        ILogger<InquirySubmissionService> logger)
    {
        _db = db;
        _runner = runner;
        _cache = cache;
        _locks = locks;
        _logger = logger;
    }

    public async Task<InquiryResultDto> SubmitAsync(InquiryRequestDto request, bool bypassCache, CancellationToken ct)
    {
        var cacheKey = ComputeCacheKey(request);

        if (!bypassCache && _cache.TryGet(cacheKey, out var cached) && cached is not null)
            return cached;

        await using var lockHandle = await _locks.LockAsync(cacheKey, ct);

        if (!bypassCache && _cache.TryGet(cacheKey, out cached) && cached is not null)
            return cached;

        var inquiry = await _db.Inquiries.Include(i => i.Attempts)
            .FirstOrDefaultAsync(i => i.CacheKey == cacheKey, ct);

        if (inquiry is null)
        {
            inquiry = new Inquiry
            {
                Id = Guid.NewGuid(),
                CacheKey = cacheKey,
                RequestPayload = JsonSerializer.Serialize(request),
                Status = InquiryStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            _db.Inquiries.Add(inquiry);
            _logger.LogInformation("Created new inquiry {InquiryId} for key {CacheKey}", inquiry.Id, cacheKey);
        }
        else if (inquiry.Status == InquiryStatus.Pending)
        {
            var peer = await WaitForPendingPeerAsync(cacheKey, ct);
            if (peer is not null)
            {
                _logger.LogInformation("Reusing result completed by another instance for key {CacheKey}", cacheKey);
                _cache.Set(cacheKey, ToResult(peer));
                return ToResult(peer);
            }

            _logger.LogWarning("Inquiry {CacheKey} was stuck pending; taking it over", cacheKey);
        }
        else if (inquiry.Status == InquiryStatus.Completed && !bypassCache)
        {
            _logger.LogInformation("Returning previously completed inquiry for key {CacheKey}", cacheKey);
            _cache.Set(cacheKey, ToResult(inquiry));
            return ToResult(inquiry);
        }
        else
        {
            var previousStatus = inquiry.Status;
            ResetForRerun(inquiry);
            _logger.LogInformation("Re-running inquiry for key {CacheKey} (bypassCache={Bypass}, previous status={Status})", cacheKey, bypassCache, previousStatus);
        }

        using (_logger.BeginScope(new Dictionary<string, object> { ["InquiryKey"] = cacheKey }))
        {
            _logger.LogInformation("Starting provider pipeline for inquiry {CacheKey}", cacheKey);

            FailoverRunnerResult outcome;
            try
            {
                outcome = await _runner.RunAsync(request, ct);
            }
            catch (OperationCanceledException)
            {
                inquiry.Status = InquiryStatus.Failed;
                inquiry.CompletedAt = DateTime.UtcNow;
                inquiry.TechnicalError = "Request was cancelled before a provider finished.";
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            ApplyOutcome(inquiry, outcome);
            await _db.SaveChangesAsync(ct);

            var result = ToResult(inquiry);
            if (inquiry.Status == InquiryStatus.Completed)
                _cache.Set(cacheKey, result);

            _logger.LogInformation("Pipeline finished for key {CacheKey}: status={Status}, serving={Provider}", cacheKey, inquiry.Status, inquiry.ServingProvider);
            return result;
        }
    }

    private async Task<Inquiry?> WaitForPendingPeerAsync(string cacheKey, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(PeerWaitDeadline);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, ct);
            var current = await _db.Inquiries.Include(i => i.Attempts).AsNoTracking()
                .FirstOrDefaultAsync(i => i.CacheKey == cacheKey, ct);

            if (current is null || current.Status != InquiryStatus.Pending)
                return current;
        }

        return null;
    }

    private static void ResetForRerun(Inquiry inquiry)
    {
        inquiry.Status = InquiryStatus.Pending;
        inquiry.CompletedAt = null;
        inquiry.ResultPayload = null;
        inquiry.ServingProvider = null;
        inquiry.IsBusinessError = false;
        inquiry.BusinessErrorCode = null;
        inquiry.BusinessErrorMessage = null;
        inquiry.TechnicalError = null;
    }

    private static void ApplyOutcome(Inquiry inquiry, FailoverRunnerResult outcome)
    {
        inquiry.CompletedAt = DateTime.UtcNow;

        foreach (var attempt in outcome.Attempts)
        {
            inquiry.Attempts.Add(new ProviderAttempt
            {
                Id = Guid.NewGuid(),
                InquiryId = inquiry.Id,
                ProviderName = attempt.ProviderName,
                AttemptOrder = attempt.AttemptOrder,
                Outcome = attempt.Outcome,
                OutcomeDetail = attempt.OutcomeDetail,
                ElapsedMs = attempt.ElapsedMs,
                AttemptedAt = DateTime.UtcNow
            });
        }

        if (outcome.AllFailed)
        {
            inquiry.Status = InquiryStatus.Failed;
            inquiry.ResultPayload = null;
            inquiry.ServingProvider = null;
            inquiry.IsBusinessError = false;
            inquiry.BusinessErrorCode = null;
            inquiry.BusinessErrorMessage = null;
            inquiry.TechnicalError = outcome.FailureReason;
            return;
        }

        inquiry.Status = InquiryStatus.Completed;
        inquiry.ResultPayload = outcome.FinalPayload;
        inquiry.ServingProvider = outcome.ServingProvider;
        inquiry.IsBusinessError = outcome.IsBusinessError;
        inquiry.BusinessErrorCode = outcome.BusinessErrorCode;
        inquiry.BusinessErrorMessage = outcome.BusinessErrorMessage;
        inquiry.TechnicalError = null;
    }

    private static InquiryResultDto ToResult(Inquiry inquiry)
    {
        JsonElement? result = null;
        if (inquiry.ResultPayload is not null)
            result = JsonSerializer.Deserialize<JsonElement>(inquiry.ResultPayload);

        var attempts = inquiry.Attempts
            .OrderBy(a => a.AttemptOrder)
            .Select(a => new ProviderAttemptDto(a.ProviderName, a.AttemptOrder, a.Outcome, a.OutcomeDetail, a.ElapsedMs))
            .ToList();

        return new InquiryResultDto(
            inquiry.Id,
            inquiry.Status.ToString(),
            result,
            inquiry.ServingProvider,
            inquiry.IsBusinessError,
            inquiry.BusinessErrorCode,
            inquiry.BusinessErrorMessage,
            inquiry.CompletedAt,
            attempts);
    }

    public static string ComputeCacheKey(InquiryRequestDto request)
    {
        var canonical = JsonSerializer.Serialize(new { request.CustomerId, request.ProductId, request.Reference });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}