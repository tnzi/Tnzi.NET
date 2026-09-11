namespace Tnzi.Logging.Dtos;

/// <summary>
/// Single log level entry: the per-level directory that LoggingModule
/// creates (Information / Warning / Error / Fatal / Debug). Used by the
/// admin log viewer to populate the left-side level navigator.
/// </summary>
public class LogLevelInfoDto
{
    /// <summary>Level name (Information / Warning / Error / Fatal / Debug).</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Whether the level's file sink is enabled in configuration.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Number of log files currently present under this level.</summary>
    public int FileCount { get; set; }

    /// <summary>Total bytes of all files under this level.</summary>
    public long TotalSize { get; set; }

    /// <summary>UTC timestamp of the most recently modified file (null if no files).</summary>
    public DateTime? LastModifiedUtc { get; set; }
}

/// <summary>
/// Individual log file metadata.
/// </summary>
public class LogFileInfoDto
{
    /// <summary>Owning level (Information / Warning / Error / Fatal / Debug).</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>File name (e.g. <c>log-20260518.txt</c>).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>UTC last-write timestamp.</summary>
    public DateTime LastModifiedUtc { get; set; }

    /// <summary>Parsed date if the file name follows the day-rolling convention.</summary>
    public DateTime? RollingDate { get; set; }
}

/// <summary>
/// Tail-read result: last N lines of a single log file.
/// </summary>
public class LogTailResultDto
{
    /// <summary>Resolved level.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Resolved file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Total file size in bytes (so the UI can show "showing last N of X bytes").</summary>
    public long TotalSize { get; set; }

    /// <summary>Lines returned (oldest first within the tail window).</summary>
    public List<string> Lines { get; set; } = new();

    /// <summary>True if the file was truncated to satisfy the line limit.</summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Search-result row across the configured time window.
/// </summary>
public class LogSearchHitDto
{
    /// <summary>Level where this hit was found.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>File where this hit lives.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>1-based line number inside the file.</summary>
    public int LineNumber { get; set; }

    /// <summary>The matching line content.</summary>
    public string Line { get; set; } = string.Empty;
}

/// <summary>
/// Search aggregate result.
/// </summary>
public class LogSearchResultDto
{
    /// <summary>Levels and date range that were searched.</summary>
    public List<string> Levels { get; set; } = new();

    /// <summary>UTC start of the search window (inclusive).</summary>
    public DateTime? FromUtc { get; set; }

    /// <summary>UTC end of the search window (inclusive).</summary>
    public DateTime? ToUtc { get; set; }

    /// <summary>The keyword that was searched (verbatim, lower-cased for the match).</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>Matching lines (capped by the service's `maxResults` parameter).</summary>
    public List<LogSearchHitDto> Hits { get; set; } = new();

    /// <summary>True when the scan stopped early for any reason (see <see cref="TruncationReason"/>).</summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// Why the scan stopped early, or <see cref="LogSearchTruncation.None"/> when
    /// the whole window was read. A budget-truncated search that reported only
    /// "no hits" would be indistinguishable from a search that genuinely found
    /// nothing, so the reason is part of the answer.
    /// </summary>
    public LogSearchTruncation TruncationReason { get; set; } = LogSearchTruncation.None;

    /// <summary>Bytes actually read while scanning (makes the budget observable).</summary>
    public long ScannedBytes { get; set; }

    /// <summary>Total milliseconds spent scanning files server-side.</summary>
    public long ElapsedMs { get; set; }
}

/// <summary>
/// Why a log search returned before reading the whole window.
/// </summary>
public enum LogSearchTruncation
{
    /// <summary>The entire requested window was scanned.</summary>
    None = 0,

    /// <summary>The <c>maxResults</c> cap was reached; more matches may exist.</summary>
    ResultLimit = 1,

    /// <summary>The configured byte budget was exhausted (<c>Logging:Search:MaxScanBytes</c>).</summary>
    ByteBudget = 2,

    /// <summary>The configured time budget was exhausted (<c>Logging:Search:MaxScanSeconds</c>).</summary>
    TimeBudget = 3,

    /// <summary>The caller cancelled (client disconnected, request aborted).</summary>
    Cancelled = 4,
}
