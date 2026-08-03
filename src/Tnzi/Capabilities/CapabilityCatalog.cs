namespace Tnzi.Capabilities;

/// <summary>
/// Default <see cref="ICapabilityCatalog"/> - a thread-safe set of declared capability names.
/// </summary>
/// <remarks>
/// Declaration happens during module service configuration, reads happen per request, so the set
/// is written on one thread and then read from many.
/// </remarks>
public sealed class CapabilityCatalog : ICapabilityCatalog
{
    private readonly ConcurrentDictionary<string, byte> _declared = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<string> ServerCapabilities
        => _declared.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    /// <inheritdoc />
    public void Declare(string capability)
    {
        Check.NotNullOrWhiteSpace(capability);

        // Fail at startup rather than shipping a name no client can ever match: a typo here is
        // invisible at runtime because "nobody declared it" and "it does not exist" look alike.
        if (!TnziCapabilities.IsValidName(capability))
        {
            throw new ArgumentException(
                $"'{capability}' is not a valid capability name. Names must be lowercase "
                + "kebab-case with a version suffix, e.g. 'chat-draft-restore-v1'.",
                nameof(capability));
        }

        _declared.TryAdd(capability, 0);
    }

    /// <inheritdoc />
    public bool IsDeclared(string capability)
        => !string.IsNullOrWhiteSpace(capability) && _declared.ContainsKey(capability);
}
