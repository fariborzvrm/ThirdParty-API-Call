using InquiryService.Api.Domain;
using InquiryService.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace InquiryService.Api.Controllers;

[ApiController]
[Route("api/inquiries")]
public sealed class InquiriesController(InquirySubmissionService inquiryService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SubmitAsync(
        [FromBody] InquiryRequestDto request,
        [FromQuery] bool bypassCache = false,
        CancellationToken ct = default)
    {
        var result = await inquiryService.SubmitAsync(request, bypassCache, ct);
        return Ok(result);
    }
}