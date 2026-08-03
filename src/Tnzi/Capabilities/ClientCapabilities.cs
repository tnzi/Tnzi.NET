namespace Tnzi.Capabilities;

/// <summary>
/// Parses a client capability declaration into an immutable, queryable set.
/// </summary>
/// <remarks>
/// Kept transport-agnostic (a plain header string in, a set out) so the parsing rules can be
/// tested without an HTTP context, and so non-HTTP transports can reuse them.
/// </remarks>
public sealed class ClientCapabilities : IClientCapabilities
{
    private static readonly char[] Separators = [',', ' '];

    private readonly HashSet<string> _declared;

    /// <summary>
    /// An empty declaration - the correct answer for any caller that declared nothing.
    /// </summary>
    public static readonly IClientCapabilities None = new ClientCapabilities(null);

    /// <summary>
    /// Parse a comma-separated capability declaration (typically the
    /// <see cref="TnziCapabilities.HeaderName"/> header value).
    /// </summary>
    /// <remarks>
    /// Malformed entries are dropped rather than rejected: a client sending one bad name should
    /// lose that one capability, not have the whole request fail. Since an unrecognised capability
    /// degrades to the older path, dropping is already the safe direction.
    /// </remarks>
    public ClientCapabilities(string? headerValue)
    {
        _declared = new HashSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(headerValue)) return;

        foreach (var raw in headerValue.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim();
            if (TnziCapabilities.IsValidName(name))
                _declared.Add(name);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Declared
        => _declared.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public bool Supports(string capability)
        => !string.IsNullOrWhiteSpace(capability) && _declared.Contains(capability);
}
