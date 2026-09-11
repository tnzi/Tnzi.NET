namespace Tnzi.Feature.Metadata;

/// <summary>
/// Where the effective value shown for a feature in a given scope comes from.
/// Serialized as the member name (global <c>JsonStringEnumConverter</c>).
/// </summary>
public enum FeatureValueSource
{
    /// <summary>No provider holds a value for this feature; the definition default applies.</summary>
    Default = 0,

    /// <summary>
    /// The requested scope has no value of its own; a lower-priority keyless provider
    /// (the deployment-wide "Global" scope) supplies it.
    /// </summary>
    Inherited = 1,

    /// <summary>The requested scope holds an explicit value for this feature.</summary>
    Explicit = 2
}
