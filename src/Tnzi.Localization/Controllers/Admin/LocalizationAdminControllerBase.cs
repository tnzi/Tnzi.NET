namespace Tnzi.Localization.Controllers.Admin;

/// <summary>
/// Localization admin controller base
/// Provides missing translation management endpoints for administrators
/// </summary>
[Route("admin/localization")]
public abstract class LocalizationAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IMissingTranslationTracker MissingTranslationTracker;

    /// <summary>
    /// Initialize localization admin controller base
    /// </summary>
    protected LocalizationAdminControllerBase(IMissingTranslationTracker missingTranslationTracker)
    {
        MissingTranslationTracker = Check.NotNull(missingTranslationTracker);
    }

    /// <summary>
    /// Get all missing translation keys, optionally filtered by culture
    /// </summary>
    [HttpGet("missing")]
    public virtual async Task<ApiResult<IReadOnlyList<MissingTranslationDto>>> GetMissingKeys([FromQuery] string? culture = null)
    {
        var result = await MissingTranslationTracker.GetMissingKeysAsync(culture);
        return Ok(result);
    }

    /// <summary>
    /// Get a summary of missing translations grouped by culture
    /// </summary>
    [HttpGet("missing/summary")]
    public virtual async Task<ApiResult<MissingTranslationSummaryDto>> GetMissingSummary()
    {
        var result = await MissingTranslationTracker.GetSummaryAsync();
        return Ok(result);
    }

    /// <summary>
    /// Export missing translations as JSON translation file stubs
    /// Grouped by culture, each key mapped to empty string for easy filling
    /// </summary>
    [HttpGet("missing/export")]
    public virtual async Task<ApiResult<Dictionary<string, Dictionary<string, string>>>> ExportMissing([FromQuery] string? culture = null)
    {
        var result = await MissingTranslationTracker.ExportMissingAsync(culture);
        return Ok(result);
    }

    /// <summary>
    /// Clear all tracked missing translations
    /// </summary>
    [HttpDelete("missing")]
    public virtual async Task<ApiResult> ClearMissing()
    {
        await MissingTranslationTracker.ClearAsync();
        return Ok();
    }
}
