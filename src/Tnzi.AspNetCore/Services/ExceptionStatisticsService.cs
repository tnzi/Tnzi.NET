
namespace Tnzi.AspNetCore.Services;

/// <summary>
/// Exception statistics service implementation
/// Wraps <see cref="IExceptionStatistics"/> to provide Result-based API for controllers
/// </summary>
public class ExceptionStatisticsService : IExceptionStatisticsService
{
    private readonly IExceptionStatistics _exceptionStatistics;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExceptionStatisticsService"/> class
    /// </summary>
    /// <param name="exceptionStatistics">Exception statistics provider</param>
    public ExceptionStatisticsService(IExceptionStatistics exceptionStatistics)
    {
        _exceptionStatistics = Check.NotNull(exceptionStatistics);
    }

    /// <inheritdoc />
    public Result<ExceptionSummaryDto> GetSummary(int minutes = 60)
    {
        if (minutes <= 0)
            return Result<ExceptionSummaryDto>.Failure("Time window must be positive", 400);

        var timeWindow = TimeSpan.FromMinutes(minutes);
        var stats = _exceptionStatistics.GetStats(timeWindow);
        var recentEntries = _exceptionStatistics.GetRecentExceptions(5);

        var dto = new ExceptionSummaryDto
        {
            TotalCount = stats.TotalCount,
            ByType = stats.ByType,
            ByStatusCode = stats.ByStatusCode,
            ByErrorCode = stats.ByErrorCode,
            TopExceptions = stats.ByType
                .OrderByDescending(kv => kv.Value)
                .Take(10)
                .Select(kv => new ExceptionTypeCountDto
                {
                    ExceptionType = kv.Key,
                    Count = kv.Value
                })
                .ToList(),
            RecentExceptions = recentEntries.Select(MapToDto).ToList(),
            Since = DateTime.UtcNow.Subtract(timeWindow)
        };

        return Result<ExceptionSummaryDto>.Success(dto);
    }

    /// <inheritdoc />
    public Result<List<ExceptionEntryDto>> GetRecentExceptions(int count = 20)
    {
        if (count <= 0)
            return Result<List<ExceptionEntryDto>>.Failure("Count must be positive", 400);

        if (count > 500)
            count = 500;

        var entries = _exceptionStatistics.GetRecentExceptions(count);
        var dtos = entries.Select(MapToDto).ToList();

        return Result<List<ExceptionEntryDto>>.Success(dtos);
    }

    /// <inheritdoc />
    public Result Clear()
    {
        _exceptionStatistics.Clear();
        return Result.Success("Exception statistics cleared");
    }

    private static ExceptionEntryDto MapToDto(ExceptionRecord record)
    {
        return new ExceptionEntryDto
        {
            ExceptionType = record.ExceptionType,
            Message = record.Message,
            StatusCode = record.StatusCode,
            ErrorCode = record.ErrorCode,
            RequestId = record.RequestId,
            Timestamp = record.Timestamp
        };
    }
}
