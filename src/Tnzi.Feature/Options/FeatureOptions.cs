namespace Tnzi.Feature.Options;

/// <summary>
/// Feature module configuration options
/// </summary>
public class FeatureOptions
{
    /// <summary>
    /// Whether to enable the feature module
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cache refresh interval in minutes (0 = no auto-refresh)
    /// </summary>
    public int CacheRefreshIntervalMinutes { get; set; } = 30;
}
