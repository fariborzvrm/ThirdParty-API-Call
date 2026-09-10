namespace InquiryService.Api.Domain;

public class Inquiry
{
    public Guid Id { get; set; }
    public string CacheKey { get; set; } = "";
    public string RequestPayload { get; set; } = "";
    public InquiryStatus Status { get; set; }
    public string? ResultPayload { get; set; }
    public string? ServingProvider { get; set; }
    public bool IsBusinessError { get; set; }
    public string? BusinessErrorCode { get; set; }
    public string? BusinessErrorMessage { get; set; }
    public string? TechnicalError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ICollection<ProviderAttempt> Attempts { get; set; } = new List<ProviderAttempt>();
}