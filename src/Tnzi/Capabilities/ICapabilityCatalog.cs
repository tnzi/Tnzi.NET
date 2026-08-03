namespace Tnzi.Capabilities;

/// <summary>
/// Registry of the capabilities this server supports. Singleton; modules declare into it during
/// service configuration.
/// </summary>
/// <remarks>
/// This answers "what does the server understand", which clients read once at startup to decide
/// whether to use a newer flow. It deliberately says nothing about any particular client - see
/// <see cref="IClientCapabilities"/> for that side, and note that the two must never be conflated.
/// </remarks>
[ExperimentalApi(Reason = "Capability negotiation is new and has no consumers yet; the shape may change once the first real cross-version capability lands.")]
public interface ICapabilityCatalog
{
    /// <summary>
    /// Capability names this server supports, sorted for stable output.
    /// </summary>
    IReadOnlyList<string> ServerCapabilities { get; }

    /// <summary>
    /// Declare a capability this server supports. Idempotent.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not a well-formed capability name.</exception>
    void Declare(string capability);

    /// <summary>
    /// Whether this server declared the given capability.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is <b>not</b> the question to ask before using a capability on a request. Use
    /// <see cref="IClientCapabilities.Supports"/> for that: the server supporting something says
    /// nothing about whether the caller can handle it.
    /// </remarks>
    bool IsDeclared(string capability);
}
