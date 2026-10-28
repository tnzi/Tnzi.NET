
namespace Tnzi.AI.Engine.Context;

/// <summary>
/// Options for <see cref="ClaimContextProvider"/>. Consumers configure which
/// JWT claims to surface into the AI system message and how to format them.
/// </summary>
public sealed class ClaimInjectionOptions
{
    /// <summary>
    /// Prefix filter for claim types. When set (e.g. "ai:"), all claims whose
    /// type starts with this prefix are included. An empty string turns the prefix filter
    /// off: only <see cref="IncludedClaims"/> are then included (it never means "all claims").
    /// </summary>
    public string ClaimPrefix { get; set; } = "ai:";

    /// <summary>
    /// Explicit whitelist of claim types to include in addition to the prefix filter.
    /// A claim matches if EITHER its type starts with <see cref="ClaimPrefix"/>
    /// OR is contained in this set. When null or empty, only the prefix filter applies.
    /// </summary>
    public IReadOnlyCollection<string>? IncludedClaims { get; set; }

    /// <summary>
    /// Format string for the injected context. Use <c>{claims}</c> as a placeholder
    /// for the comma-separated key=value list of matched claims.
    /// </summary>
    public string TemplateFormat { get; set; } = "User preferences: {claims}";
}
