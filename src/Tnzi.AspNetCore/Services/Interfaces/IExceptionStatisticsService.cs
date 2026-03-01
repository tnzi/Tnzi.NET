
namespace Tnzi.AspNetCore.Services;

/// <summary>
/// Exception statistics service interface for admin API
/// Wraps <see cref="IExceptionStatistics"/> with Result-based methods for controller consumption
/// </summary>
public interface IExceptionStatisticsService
{
    /// <summary>
    /// Get exception summary within a time window
    /// </summary>
    /// <param name="minutes">Time window in minutes (default: 60)</param>
    /// <returns>Exception summary</returns>
    Result<ExceptionSummaryDto> GetSummary(int minutes = 60);

    /// <summary>
    /// Get recent exception entries
    /// </summary>
    /// <param name="count">Number of recent entries to return (default: 20)</param>
    /// <returns>List of recent exception entries</returns>
    Result<List<ExceptionEntryDto>> GetRecentExceptions(int count = 20);

    /// <summary>
    /// Clear all exception statistics
    /// </summary>
    /// <returns>Operation result</returns>
    Result Clear();
}
