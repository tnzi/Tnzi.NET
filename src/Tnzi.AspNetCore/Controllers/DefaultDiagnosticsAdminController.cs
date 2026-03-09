
namespace Tnzi.AspNetCore.Controllers;

/// <summary>
/// Diagnostics admin controller base class
/// Provides exception statistics query and management endpoints
/// </summary>
[Route("admin/diagnostics")]
[DefaultController]
public class DefaultDiagnosticsAdminController : ApiAdminControllerBase
{
    protected readonly IExceptionStatisticsService ExceptionStatisticsService;

    /// <summary>
    /// Initializes the diagnostics admin controller
    /// </summary>
    /// <param name="exceptionStatisticsService">Exception statistics service</param>
    public DefaultDiagnosticsAdminController(IExceptionStatisticsService exceptionStatisticsService)
    {
        ExceptionStatisticsService = Check.NotNull(exceptionStatisticsService);
    }

    /// <summary>
    /// Get exception summary within a time window
    /// </summary>
    /// <param name="minutes">Time window in minutes (default: 60)</param>
    /// <returns>Exception summary including totals, breakdowns, and top exceptions</returns>
    [HttpGet("exceptions/summary")]
    public virtual ApiResult<ExceptionSummaryDto> GetExceptionSummary([FromQuery] int minutes = 60)
    {
        var result = ExceptionStatisticsService.GetSummary(minutes);
        return result.ToApiResult();
    }

    /// <summary>
    /// Get recent exception entries
    /// </summary>
    /// <param name="count">Number of recent entries to return (default: 20, max: 500)</param>
    /// <returns>List of recent exception entries</returns>
    [HttpGet("exceptions/recent")]
    public virtual ApiResult<List<ExceptionEntryDto>> GetRecentExceptions([FromQuery] int count = 20)
    {
        var result = ExceptionStatisticsService.GetRecentExceptions(count);
        return result.ToApiResult();
    }

    /// <summary>
    /// Clear all exception statistics
    /// </summary>
    /// <returns>Operation result</returns>
    [HttpDelete("exceptions")]
    public virtual ApiResult ClearExceptions()
    {
        var result = ExceptionStatisticsService.Clear();
        return result.ToApiResult();
    }
}
