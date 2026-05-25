using Tnzi.Logging.Dtos;
using Tnzi.Logging.Services.Interfaces;

namespace Tnzi.AspNetCore.Controllers;

/// <summary>
/// Admin-facing read access to the on-disk log files produced by
/// <c>Tnzi.Logging</c>'s Serilog file sinks. Routes:
///   GET /admin/logs/levels                       — list level dirs + summary
///   GET /admin/logs/files?level=Error            — list files in a level
///   GET /admin/logs/tail?level=Error&amp;file=...   — last N lines
///   GET /admin/logs/search?keyword=...&amp;level=…  — case-insensitive grep
///
/// All endpoints are read-only — there is no delete/edit because the log
/// files are runtime artefacts owned by Serilog (retention is configured
/// server-side via <c>LoggingOptions.FileOutput.*.RetainedFileCountLimit</c>).
/// </summary>
[Route("admin/logs")]
[DefaultController]
public class DefaultLogFileAdminController : ApiAdminControllerBase
{
    protected readonly ILogFileService LogFileService;

    public DefaultLogFileAdminController(ILogFileService logFileService)
    {
        LogFileService = Check.NotNull(logFileService);
    }

    /// <summary>List every configured log level + summary stats.</summary>
    [HttpGet("levels")]
    public virtual async Task<ApiResult<List<LogLevelInfoDto>>> GetLevels(CancellationToken cancellationToken = default)
    {
        var result = await LogFileService.GetLevelsAsync(cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>List log files under a single level, newest first.</summary>
    [HttpGet("files")]
    public virtual async Task<ApiResult<List<LogFileInfoDto>>> GetFiles([FromQuery] string level, CancellationToken cancellationToken = default)
    {
        var result = await LogFileService.GetFilesAsync(level, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>Tail the last N lines of a single log file.</summary>
    [HttpGet("tail")]
    public virtual async Task<ApiResult<LogTailResultDto>> Tail(
        [FromQuery] string level,
        [FromQuery] string file,
        [FromQuery] int lines = 500,
        CancellationToken cancellationToken = default)
    {
        var result = await LogFileService.TailAsync(level, file, lines, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>Case-insensitive keyword search across log files.</summary>
    [HttpGet("search")]
    public virtual async Task<ApiResult<LogSearchResultDto>> Search(
        [FromQuery] string keyword,
        [FromQuery] string? level = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        var result = await LogFileService.SearchAsync(keyword, level, fromUtc, toUtc, maxResults, cancellationToken);
        return result.ToApiResult();
    }
}
