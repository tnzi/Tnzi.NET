namespace Tnzi.Localization.Services;

/// <summary>
/// Missing translation tracker implementation
/// Tracks missing translation keys with access count and timing statistics.
/// Thread-safe using ConcurrentDictionary. In development mode, also logs warnings.
/// </summary>
public class MissingTranslationTracker : IMissingTranslationTracker
{
    private readonly ILogger<MissingTranslationTracker> _logger;
    private readonly bool _isDevelopment;
    private readonly ConcurrentDictionary<string, MissingTranslationEntry> _missingEntries = new();

    public MissingTranslationTracker(ILogger<MissingTranslationTracker> logger, IHostEnvironment environment)
    {
        _logger = Check.NotNull(logger);
        _isDevelopment = Check.NotNull(environment).IsDevelopment();
    }

    /// <summary>
    /// Track a missing translation key with count and timing
    /// In development mode, also logs a warning (once per key)
    /// </summary>
    public void TrackMissing(string culture, string key)
    {
        var cacheKey = $"{culture}:{key}";
        var now = DateTime.UtcNow;

        _missingEntries.AddOrUpdate(
            cacheKey,
            _ =>
            {
                // Log warning in development mode on first access
                if (_isDevelopment)
                {
                    _logger.LogWarning("Missing translation for culture '{Culture}', key '{Key}'", culture, key);
                }

                return new MissingTranslationEntry
                {
                    Culture = culture,
                    Key = key,
                    _accessCount = 1,
                    FirstAccessTime = now,
                    LastAccessTime = now
                };
            },
            (_, existing) =>
            {
                Interlocked.Increment(ref existing._accessCount);
                existing.LastAccessTime = now;
                return existing;
            });
    }

    /// <summary>
    /// Get all missing translation keys, optionally filtered by culture
    /// </summary>
    public Task<IReadOnlyList<MissingTranslationDto>> GetMissingKeysAsync(string? culture = null)
    {
        var entries = _missingEntries.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(culture))
        {
            entries = entries.Where(e => string.Equals(e.Culture, culture, StringComparison.OrdinalIgnoreCase));
        }

        var result = entries
            .OrderByDescending(e => e.LastAccessTime)
            .Select(e => new MissingTranslationDto
            {
                Culture = e.Culture,
                Key = e.Key,
                AccessCount = e.AccessCount,
                FirstAccessTime = e.FirstAccessTime,
                LastAccessTime = e.LastAccessTime
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<MissingTranslationDto>>(result);
    }

    /// <summary>
    /// Get a summary of missing translations grouped by culture
    /// </summary>
    public Task<MissingTranslationSummaryDto> GetSummaryAsync()
    {
        var entries = _missingEntries.Values.ToList();

        var cultureBreakdown = entries
            .GroupBy(e => e.Culture)
            .Select(g => new CultureMissingCountDto
            {
                Culture = g.Key,
                MissingKeyCount = g.Count(),
                TotalAccessCount = g.Sum(e => e.AccessCount)
            })
            .OrderByDescending(c => c.MissingKeyCount)
            .ToList();

        var summary = new MissingTranslationSummaryDto
        {
            TotalMissingKeys = entries.Count,
            TotalAccessCount = entries.Sum(e => e.AccessCount),
            AffectedCultureCount = cultureBreakdown.Count,
            CultureBreakdown = cultureBreakdown
        };

        return Task.FromResult(summary);
    }

    /// <summary>
    /// Export missing translations in a format suitable for generating translation file stubs
    /// Returns culture -> (key -> empty string) dictionary
    /// </summary>
    public Task<Dictionary<string, Dictionary<string, string>>> ExportMissingAsync(string? culture = null)
    {
        var entries = _missingEntries.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(culture))
        {
            entries = entries.Where(e => string.Equals(e.Culture, culture, StringComparison.OrdinalIgnoreCase));
        }

        var result = entries
            .GroupBy(e => e.Culture)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.Key).ToDictionary(e => e.Key, _ => string.Empty));

        return Task.FromResult(result);
    }

    /// <summary>
    /// Clear all tracked missing translations
    /// </summary>
    public Task ClearAsync()
    {
        _missingEntries.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Internal entry for tracking missing translation statistics
    /// </summary>
    private class MissingTranslationEntry
    {
        public string Culture { get; init; } = null!;
        public string Key { get; init; } = null!;
        internal int _accessCount;
        public int AccessCount => _accessCount;
        public DateTime FirstAccessTime { get; init; }
        public DateTime LastAccessTime { get; set; }
    }
}
