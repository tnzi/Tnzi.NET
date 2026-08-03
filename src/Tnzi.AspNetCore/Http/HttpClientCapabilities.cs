namespace Tnzi.AspNetCore.Http;

/// <summary>
/// Resolves the caller's declared capabilities from the current HTTP request header.
/// </summary>
/// <remarks>
/// Registered scoped: the declaration belongs to one request, and caching it across requests would
/// leak one caller's capabilities into another's.
/// <para>
/// With no HTTP context (background jobs, hosted services, tests) the answer is
/// <see cref="ClientCapabilities.None"/> - nothing declared, so every capability check says no and
/// the older path is taken. That is the safe direction: work with no caller cannot consent to a
/// newer protocol on a client's behalf.
/// </para>
/// </remarks>
public sealed class HttpClientCapabilities : IClientCapabilities
{
    private readonly IClientCapabilities _resolved;

    /// <summary>
    /// Initializes capabilities from the current request, if any.
    /// </summary>
    public HttpClientCapabilities(IHttpContextAccessor httpContextAccessor)
    {
        Check.NotNull(httpContextAccessor);

        var header = httpContextAccessor.HttpContext?.Request.Headers[TnziCapabilities.HeaderName];

        _resolved = header is null || header.Value.Count == 0
            ? ClientCapabilities.None
            : new ClientCapabilities(string.Join(',', header.Value.ToArray()));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Declared => _resolved.Declared;

    /// <inheritdoc />
    public bool Supports(string capability) => _resolved.Supports(capability);
}
