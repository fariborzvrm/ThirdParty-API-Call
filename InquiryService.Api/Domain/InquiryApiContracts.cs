using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace InquiryService.Api.Domain;

public sealed record InquiryRequestDto
{
    [Required, MaxLength(64)]
    public string CustomerId { get; init; } = "";

    [Required, MaxLength(64)]
    public string ProductId { get; init; } = "";

    [MaxLength(64)]
    public string? Reference { get; init; }

    public override string ToString() => $"{CustomerId}|{ProductId}|{Reference}";
}

public sealed record ProviderAttemptDto(
    string ProviderName,
    int AttemptOrder,
    string Outcome,
    string? OutcomeDetail,
    long ElapsedMs);

public sealed record InquiryResultDto(
    Guid InquiryId,
    string Status,
    JsonElement? Result,
    string? ServingProvider,
    bool IsBusinessError,
    string? BusinessErrorCode,
    string? BusinessErrorMessage,
    DateTime? CompletedAt,
    IReadOnlyList<ProviderAttemptDto> Attempts);