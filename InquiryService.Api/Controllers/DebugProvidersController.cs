using InquiryService.Api.Options;
using InquiryService.Api.Providers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InquiryService.Api.Controllers;

/// <summary>
/// Development-only helpers for driving the fake providers so failover behaviour can be
/// demonstrated end to end without swapping in real HTTP calls.
/// </summary>
[ApiController]
[Route("api/debug/providers")]
public sealed class DebugProvidersController(
    FakeProviderRuntime runtime,
    IOptions<ProviderOptions> options,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public IActionResult List()
    {
        if (!environment.IsDevelopment())
            return NotFound();
        return Ok(options.Value.Providers.Select(p => new
        {
            name = p.Key,
            priority = p.Value.Priority,
            timeoutSeconds = p.Value.TimeoutSeconds,
            scenario = runtime.GetScenario(p.Key, p.Value)
        }));
    }

    [HttpPut("{providerName}/scenario")]
    public IActionResult SetScenario([FromRoute] string providerName, [FromQuery] string scenario)
    {
        if (!environment.IsDevelopment())
            return NotFound();
        if (!options.Value.Providers.ContainsKey(providerName))
            return NotFound($"Unknown provider '{providerName}'.");

        if (!IsValidScenario(scenario))
            return BadRequest("Scenario must be one of: Success, BusinessError, TechnicalFailure, Timeout.");

        runtime.SetScenario(providerName, scenario);
        return NoContent();
    }

    public static bool IsValidScenario(string scenario) => scenario is
        "Success" or "BusinessError" or "TechnicalFailure" or "Timeout";
}