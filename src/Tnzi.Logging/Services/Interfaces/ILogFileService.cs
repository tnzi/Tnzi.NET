using Tnzi.Logging.Dtos;
using Tnzi.Results;

namespace Tnzi.Logging.Services.Interfaces;

/// <summary>
/// Admin-facing read-only access to log files produced by the Serilog
/// file sinks configured in <see cref="LoggingModule"/>. Powers the
/// <c>admin/logs</c> controller endpoints.
///
/// All paths are confined under <c>LoggingOptions.BasePath</c> via
/// canonicalised path validation; directory-traversal attempts (e.g.
/// <c>../etc/passwd</c>) are rejected with a 400-style <c>Result.Fail</c>.
/// </summary>
public interface ILogFileService
{
    /// <summary>
    /// List every configured level directory plus an at-a-glance summary
    /// (file count, total size, last-modified timestamp).
    /// </summary>
    Task<Result<List<LogLevelInfoDto>>> GetLevelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// List log files under a single level, sorted by last-modified (newest
    /// first). Used by the date-picker UI to surface the per-day rollover
    /// files Serilog writes (<c>log-YYYYMMDD.txt</c>).
    /// </summary>
    Task<Result<List<LogFileInfoDto>>> GetFilesAsync(string level, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the last <paramref name="lines"/> lines of a single log file.
    /// Implementation streams from the end of the file backwards to avoid
    /// loading large files into memory. <paramref name="lines"/> is clamped
    /// to <c>[1, 5000]</c>.
    /// </summary>
    Task<Result<LogTailResultDto>> TailAsync(string level, string fileName, int lines = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grep across log files for a case-insensitive keyword, returning the
    /// first <paramref name="maxResults"/> matching lines. Optional level
    /// and date-range filters narrow the search; passing none searches all
    /// enabled levels across the entire retention window.
    ///
    /// `maxResults` is clamped to `[1, 1000]` to bound response size; the
    /// service stops scanning as soon as the cap is hit (returns <see cref="LogSearchResultDto.Truncated"/>).
    /// </summary>
    Task<Result<LogSearchResultDto>> SearchAsync(
        string keyword,
        string? level = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int maxResults = 200,
        CancellationToken cancellationToken = default);
}
