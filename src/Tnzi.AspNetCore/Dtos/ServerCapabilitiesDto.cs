namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// The capabilities this server understands. Serialization shape for <c>GET /capabilities</c>.
/// </summary>
public class ServerCapabilitiesDto
{
    /// <summary>Capability names the server supports, sorted. Empty when none are declared.</summary>
    public List<string> Capabilities { get; set; } = [];
}
