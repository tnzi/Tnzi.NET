namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Names of the Polly-wrapped HttpClients registered by <see cref="AIModule"/>.
/// Each provider configured under AI:Providers gets its own named client so that
/// circuit-breaker state is isolated per provider (a 429 on one provider cannot
/// trip the circuit of another).
/// </summary>
/// <remarks>
/// <see cref="For"/> only hands out a dedicated name that <see cref="Register"/> has seen.
/// <c>IHttpClientFactory.CreateClient</c> returns a bare client for any name nobody registered,
/// with no exception and no log line, so a provider that exists only as a database row
/// (or overrides a config entry that is disabled) used to get no HTTP-level retry, circuit breaker
/// or timeout at all while the registration comment promised it the shared Fallback pipeline.
/// </remarks>
public static class ResilientHttpClientNames
{
    public const string Fallback = "Tnzi.AI.Resilient";

    private static readonly ConcurrentDictionary<string, byte> RegisteredNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that <see cref="AIModule"/> registered a dedicated resilient client for this provider.</summary>
    public static void Register(string providerName)
    {
        Check.NotNullOrWhiteSpace(providerName);
        RegisteredNames[providerName] = 0;
    }

    /// <summary>Dedicated client name when one was registered for the provider, otherwise the shared Fallback pipeline.</summary>
    public static string For(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName) || !RegisteredNames.ContainsKey(providerName)
            ? Fallback
            : $"{Fallback}:{providerName}";
}
