namespace Tnzi.Capabilities;

/// <summary>
/// The single place where capability names are defined.
/// </summary>
/// <remarks>
/// A capability is a named, versioned protocol feature that <b>both ends must agree on before it
/// is used</b>. The framework ships as NuGet packages and <c>@tnzi/*</c> ships as npm packages, so
/// the two sides upgrade independently: a server may be newer than its clients, or older.
/// <para>
/// <b>Capability negotiation is not the same as defensive parsing.</b> Defensive parsing keeps an
/// old client from crashing on a payload it does not recognise. Negotiation is what lets a new
/// behaviour be turned on safely - it answers "may I use this yet?", which tolerance cannot.
/// </para>
/// <para>
/// <b>Most changes need neither.</b> Adding a field to a response needs nothing: clients that do
/// not know it ignore it. Negotiation is for changes where the <i>server</i> must behave
/// differently depending on what the client understands - a new streaming frame shape, a request
/// format that replaces an older one, a flow with extra round-trips.
/// </para>
/// <para>
/// <b>Names are append-only.</b> Every name carries a <c>-vN</c> suffix; changing what a name
/// means breaks exactly the deployments the mechanism exists to protect. To change semantics,
/// declare <c>-v2</c> and leave <c>-v1</c> alone until no supported client declares it.
/// </para>
/// </remarks>
public static class TnziCapabilities
{
    /// <summary>
    /// Request header through which a client declares the capabilities it understands
    /// (comma-separated).
    /// </summary>
    public const string HeaderName = "X-Tnzi-Capabilities";

    /// <summary>
    /// Capability names must be lowercase kebab-case ending in a version suffix,
    /// e.g. <c>chat-draft-restore-v1</c>.
    /// </summary>
    /// <remarks>
    /// Enforced at declaration time so a malformed name fails at startup rather than becoming a
    /// capability nobody can ever match. The version suffix is mandatory because a name without
    /// one invites redefining it in place, which is the one thing negotiation cannot survive.
    /// </remarks>
    public static readonly Regex NamePattern = BuildNamePattern();

    /// <summary>
    /// Whether the given string is a well-formed capability name.
    /// </summary>
    public static bool IsValidName(string? name)
        => !string.IsNullOrWhiteSpace(name) && NamePattern.IsMatch(name);

    private static Regex BuildNamePattern() => new(
        "^[a-z0-9]+(-[a-z0-9]+)*-v[1-9][0-9]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
}
