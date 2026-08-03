using Microsoft.AspNetCore.Authorization;

namespace Tnzi.AspNetCore.Controllers;

/// <summary>
/// Publishes the server's capability list so clients can decide which protocol paths are safe to use.
/// </summary>
/// <remarks>
/// <b>Anonymous by design.</b> A client needs to know the server's protocol surface before it can
/// pick a login flow, so gating this behind authentication would create a chicken-and-egg problem
/// for exactly the changes negotiation exists to support. The payload is a list of protocol
/// feature names - it grants nothing and identifies no one.
/// <para>
/// The mirror-image direction (what the <i>client</i> supports) is not an endpoint: it rides along
/// on every request in the <see cref="TnziCapabilities.HeaderName"/> header, because it has to be
/// known per request, not per deployment.
/// </para>
/// </remarks>
[Route("capabilities")]
[DefaultController]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultCapabilitiesController : ApiControllerBase
{
    protected readonly ICapabilityCatalog Catalog;

    /// <summary>
    /// Initializes the capabilities controller.
    /// </summary>
    public DefaultCapabilitiesController(ICapabilityCatalog catalog)
    {
        Catalog = Check.NotNull(catalog);
    }

    /// <summary>
    /// Get the capabilities this server supports.
    /// </summary>
    [HttpGet]
    public virtual ApiResult<ServerCapabilitiesDto> GetCapabilities()
        => Ok(new ServerCapabilitiesDto { Capabilities = [.. Catalog.ServerCapabilities] });
}
