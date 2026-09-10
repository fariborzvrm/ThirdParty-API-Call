namespace InquiryService.Api.Domain;

public class ProviderAttempt
{
    public Guid Id { get; set; }
    public Guid InquiryId { get; set; }
    public Inquiry Inquiry { get; set; } = null!;
    public string ProviderName { get; set; } = "";
    public int AttemptOrder { get; set; }
    public string Outcome { get; set; } = "";
    public string? OutcomeDetail { get; set; }
    public long ElapsedMs { get; set; }
    public DateTime AttemptedAt { get; set; }
}