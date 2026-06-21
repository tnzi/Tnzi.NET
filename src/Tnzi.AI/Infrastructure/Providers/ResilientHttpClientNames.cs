namespace Tnzi.AI.Infrastructure.Providers;

/// <summary>
/// Names of the Polly-wrapped HttpClients registered by <see cref="AIModule"/>.
/// Each provider configured under AI:Providers gets its own named client so that
/// circuit-breaker state is isolated per provider (a 429 on one provider cannot
/// trip the circuit of another).
/// </summary>
public static class ResilientHttpClientNames
{
    public const string Fallback = "Tnzi.AI.Resilient";

    public static string For(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName) ? Fallback : $"{Fallback}:{providerName}";
}
