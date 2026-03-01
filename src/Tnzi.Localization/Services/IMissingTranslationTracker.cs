namespace Tnzi.Localization.Services;

/// <summary>
/// Missing translation tracker interface
/// Records and reports missing translation keys with access statistics
/// </summary>
public interface IMissingTranslationTracker
{
    /// <summary>
    /// Track a missing translation key
    /// </summary>
    /// <param name="culture">Culture name (e.g., "en", "zh-CN")</param>
    /// <param name="key">The missing translation key</param>
    void TrackMissing(string culture, string key);

    /// <summary>
    /// Get all missing translation keys, optionally filtered by culture
    /// </summary>
    /// <param name="culture">Culture name to filter by, or null for all cultures</param>
    /// <returns>List of missing translation entries with statistics</returns>
    Task<IReadOnlyList<MissingTranslationDto>> GetMissingKeysAsync(string? culture = null);

    /// <summary>
    /// Get a summary of missing translations grouped by culture
    /// </summary>
    /// <returns>Summary with per-culture counts</returns>
    Task<MissingTranslationSummaryDto> GetSummaryAsync();

    /// <summary>
    /// Export missing translations in a JSON-compatible format grouped by culture
    /// Useful for generating translation file stubs
    /// </summary>
    /// <param name="culture">Culture name to filter by, or null for all cultures</param>
    /// <returns>Dictionary of culture -> (key -> empty string) for export</returns>
    Task<Dictionary<string, Dictionary<string, string>>> ExportMissingAsync(string? culture = null);

    /// <summary>
    /// Clear all tracked missing translations
    /// </summary>
    Task ClearAsync();
}
