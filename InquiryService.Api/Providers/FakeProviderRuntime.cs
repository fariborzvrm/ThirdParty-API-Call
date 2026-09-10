using System.Collections.Concurrent;
using InquiryService.Api.Options;

namespace InquiryService.Api.Providers;

/// <summary>
/// In-memory toggle for fake provider behaviour. Config in appsettings.json gives the default
/// scenario per provider; tests and the debug endpoint can override it at runtime.
/// </summary>
public sealed class FakeProviderRuntime
{
    private readonly ConcurrentDictionary<string, string> _overrides = new();

    public string GetScenario(string providerName, ProviderConfig config)
        => _overrides.TryGetValue(providerName, out var scenario) ? scenario : config.Scenario;

    public void SetScenario(string providerName, string scenario) => _overrides[providerName] = scenario;

    public void ResetScenario(string providerName) => _overrides.TryRemove(providerName, out _);
}