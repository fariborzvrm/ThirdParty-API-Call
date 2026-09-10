using InquiryService.Api.Domain;
using InquiryService.Api.Services;

namespace InquiryService.Tests;

public sealed class CacheKeyTests
{
    private static InquiryRequestDto Request(string reference = "R1") => new()
    {
        CustomerId = "C1",
        ProductId = "P1",
        Reference = reference
    };

    [Fact]
    public void Same_payload_produces_the_same_key()
    {
        Assert.Equal(InquirySubmissionService.ComputeCacheKey(Request()), InquirySubmissionService.ComputeCacheKey(Request()));
    }

    [Fact]
    public void Different_reference_produces_a_different_key()
    {
        Assert.NotEqual(
            InquirySubmissionService.ComputeCacheKey(Request("R1")),
            InquirySubmissionService.ComputeCacheKey(Request("R2")));
    }

    [Fact]
    public void Different_customer_produces_a_different_key()
    {
        var a = Request() with { CustomerId = "C1" };
        var b = Request() with { CustomerId = "C2" };

        Assert.NotEqual(
            InquirySubmissionService.ComputeCacheKey(a),
            InquirySubmissionService.ComputeCacheKey(b));
    }

    [Fact]
    public void Key_is_a_fixed_size_hash()
    {
        var key = InquirySubmissionService.ComputeCacheKey(Request());
        Assert.Equal(64, key.Length);
    }
}