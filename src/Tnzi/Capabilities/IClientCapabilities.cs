namespace Tnzi.Capabilities;

/// <summary>
/// What the <b>current caller</b> declared it understands. Scoped to the request.
/// </summary>
/// <remarks>
/// This is the only correct thing to consult before enabling a newer behaviour for a request.
/// <para>
/// <b>The server must never infer client support from its own capabilities.</b> That inference is
/// the failure this whole mechanism exists to prevent: the server is upgraded first, concludes
/// "I support it, so we can use it", and breaks every client that has not caught up - which is
/// precisely the population at risk.
/// </para>
/// <para>
/// <b>Absence means no.</b> A caller that declared nothing - an old client, a curl script, a
/// server-side job with no HTTP context - gets <c>false</c> for everything and therefore the older
/// path. Silence must never be read as consent.
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Capability negotiation is new and has no consumers yet; the shape may change once the first real cross-version capability lands.")]
public interface IClientCapabilities
{
    /// <summary>
    /// Capability names the caller declared, sorted for stable output. Empty when none were declared.
    /// </summary>
    IReadOnlyList<string> Declared { get; }

    /// <summary>
    /// Whether the caller declared support for the given capability.
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> for unknown, malformed, or undeclared names - a request may only opt
    /// into a newer path by saying so.
    /// </remarks>
    bool Supports(string capability);
}
