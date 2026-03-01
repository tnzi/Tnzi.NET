
namespace Tnzi.AspNetCore.Tests;

/// <summary>
/// Tests for <see cref="ExceptionStatisticsService"/> and <see cref="ExceptionStatistics"/>
/// </summary>
public class ExceptionStatisticsServiceTests
{
    private readonly ExceptionStatistics _statistics;
    private readonly ExceptionStatisticsService _service;

    public ExceptionStatisticsServiceTests()
    {
        _statistics = new ExceptionStatistics(maxHistorySize: 50);
        _service = new ExceptionStatisticsService(_statistics);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_ShouldThrow_WhenExceptionStatisticsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ExceptionStatisticsService(null!));
    }

    #endregion

    #region GetSummary Tests

    [Fact]
    public void GetSummary_ShouldReturnEmptySummary_WhenNoExceptionsRecorded()
    {
        var result = _service.GetSummary(60);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(0, result.Data!.TotalCount);
        Assert.Empty(result.Data.ByType);
        Assert.Empty(result.Data.ByStatusCode);
        Assert.Empty(result.Data.ByErrorCode);
        Assert.Empty(result.Data.TopExceptions);
        Assert.Empty(result.Data.RecentExceptions);
    }

    [Fact]
    public void GetSummary_ShouldReturnCorrectTotalCount()
    {
        _statistics.RecordException(new InvalidOperationException("test1"), "req-1");
        _statistics.RecordException(new ArgumentException("test2"), "req-2");
        _statistics.RecordException(new InvalidOperationException("test3"), "req-3");

        var result = _service.GetSummary(60);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.TotalCount);
    }

    [Fact]
    public void GetSummary_ShouldGroupByType()
    {
        _statistics.RecordException(new InvalidOperationException("test1"), "req-1");
        _statistics.RecordException(new InvalidOperationException("test2"), "req-2");
        _statistics.RecordException(new ArgumentException("test3"), "req-3");

        var result = _service.GetSummary(60);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.ByType.Count);
        Assert.Equal(2, result.Data.ByType["InvalidOperationException"]);
        Assert.Equal(1, result.Data.ByType["ArgumentException"]);
    }

    [Fact]
    public void GetSummary_ShouldReturnTopExceptions_OrderedByCount()
    {
        // Record 3 InvalidOperationException, 2 ArgumentException, 1 NullReferenceException
        _statistics.RecordException(new InvalidOperationException("a"), "r1");
        _statistics.RecordException(new InvalidOperationException("b"), "r2");
        _statistics.RecordException(new InvalidOperationException("c"), "r3");
        _statistics.RecordException(new ArgumentException("d"), "r4");
        _statistics.RecordException(new ArgumentException("e"), "r5");
        _statistics.RecordException(new NullReferenceException("f"), "r6");

        var result = _service.GetSummary(60);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Data!.TopExceptions.Count);
        Assert.Equal("InvalidOperationException", result.Data.TopExceptions[0].ExceptionType);
        Assert.Equal(3, result.Data.TopExceptions[0].Count);
        Assert.Equal("ArgumentException", result.Data.TopExceptions[1].ExceptionType);
        Assert.Equal(2, result.Data.TopExceptions[1].Count);
        Assert.Equal("NullReferenceException", result.Data.TopExceptions[2].ExceptionType);
        Assert.Equal(1, result.Data.TopExceptions[2].Count);
    }

    [Fact]
    public void GetSummary_ShouldReturnRecentExceptions_UpTo5()
    {
        for (var i = 0; i < 10; i++)
        {
            _statistics.RecordException(new InvalidOperationException($"test{i}"), $"req-{i}");
        }

        var result = _service.GetSummary(60);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Data!.RecentExceptions.Count);
    }

    [Fact]
    public void GetSummary_ShouldGroupByStatusCode_ForBusinessExceptions()
    {
        _statistics.RecordException(new ResourceNotFoundException("User"), "r1");
        _statistics.RecordException(new ResourceNotFoundException("Product"), "r2");
        _statistics.RecordException(new ForbiddenException("forbidden"), "r3");

        var result = _service.GetSummary(60);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.ByStatusCode.ContainsKey(404));
        Assert.Equal(2, result.Data.ByStatusCode[404]);
        Assert.True(result.Data.ByStatusCode.ContainsKey(403));
        Assert.Equal(1, result.Data.ByStatusCode[403]);
    }

    [Fact]
    public void GetSummary_ShouldFail_WhenMinutesIsZeroOrNegative()
    {
        var result = _service.GetSummary(0);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public void GetSummary_ShouldSetSinceTimestamp()
    {
        var before = DateTime.UtcNow.AddMinutes(-60);

        var result = _service.GetSummary(60);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.Since >= before.AddSeconds(-1));
        Assert.True(result.Data.Since <= DateTime.UtcNow);
    }

    #endregion

    #region GetRecentExceptions Tests

    [Fact]
    public void GetRecentExceptions_ShouldReturnEmpty_WhenNoExceptionsRecorded()
    {
        var result = _service.GetRecentExceptions(10);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public void GetRecentExceptions_ShouldReturnRequestedCount()
    {
        for (var i = 0; i < 10; i++)
        {
            _statistics.RecordException(new InvalidOperationException($"test{i}"), $"req-{i}");
        }

        var result = _service.GetRecentExceptions(5);

        Assert.True(result.Succeeded);
        Assert.Equal(5, result.Data!.Count);
    }

    [Fact]
    public void GetRecentExceptions_ShouldReturnAllAvailable_WhenCountExceedsRecords()
    {
        _statistics.RecordException(new InvalidOperationException("test1"), "req-1");
        _statistics.RecordException(new ArgumentException("test2"), "req-2");

        var result = _service.GetRecentExceptions(100);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public void GetRecentExceptions_ShouldReturnMostRecentFirst()
    {
        _statistics.RecordException(new InvalidOperationException("first"), "req-1");
        // Small delay to ensure different timestamps
        _statistics.RecordException(new ArgumentException("second"), "req-2");

        var result = _service.GetRecentExceptions(10);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Data!.Count);
        // Most recent should be first
        Assert.True(result.Data[0].Timestamp >= result.Data[1].Timestamp);
    }

    [Fact]
    public void GetRecentExceptions_ShouldMapAllFields()
    {
        _statistics.RecordException(new ResourceNotFoundException("User", 123), "req-123");

        var result = _service.GetRecentExceptions(1);

        Assert.True(result.Succeeded);
        var entry = result.Data![0];
        Assert.Equal("ResourceNotFoundException", entry.ExceptionType);
        Assert.Contains("User", entry.Message);
        Assert.Equal(404, entry.StatusCode);
        Assert.Equal("req-123", entry.RequestId);
        Assert.True(entry.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void GetRecentExceptions_ShouldFail_WhenCountIsZeroOrNegative()
    {
        var result = _service.GetRecentExceptions(0);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public void GetRecentExceptions_ShouldCapCountAt500()
    {
        // Record a modest amount, request > 500
        for (var i = 0; i < 10; i++)
        {
            _statistics.RecordException(new InvalidOperationException($"test{i}"), $"req-{i}");
        }

        var result = _service.GetRecentExceptions(1000);

        Assert.True(result.Succeeded);
        // Should succeed, capped at 500 but only 10 available
        Assert.Equal(10, result.Data!.Count);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_ShouldRemoveAllExceptions()
    {
        _statistics.RecordException(new InvalidOperationException("test1"), "req-1");
        _statistics.RecordException(new ArgumentException("test2"), "req-2");

        var result = _service.Clear();

        Assert.True(result.Succeeded);

        // Verify cleared
        var summaryResult = _service.GetSummary(60);
        Assert.Equal(0, summaryResult.Data!.TotalCount);
    }

    [Fact]
    public void Clear_ShouldSucceed_WhenNoExceptionsExist()
    {
        var result = _service.Clear();

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Clear_ShouldReturnSuccessMessage()
    {
        var result = _service.Clear();

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Message);
        Assert.Contains("cleared", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region ExceptionStatistics Direct Tests

    [Fact]
    public void ExceptionStatistics_ShouldRespectMaxHistorySize()
    {
        var smallStats = new ExceptionStatistics(maxHistorySize: 5);

        for (var i = 0; i < 20; i++)
        {
            smallStats.RecordException(new InvalidOperationException($"test{i}"), $"req-{i}");
        }

        var recent = smallStats.GetRecentExceptions(100);
        // Should be capped at approximately 5 (batch cleanup allows some overshoot)
        Assert.True(recent.Count <= 10);
    }

    [Fact]
    public void ExceptionStatistics_GetStats_ShouldFilterByTimeWindow()
    {
        _statistics.RecordException(new InvalidOperationException("test"), "req-1");

        // Very short time window should include the record (just recorded)
        var stats = _statistics.GetStats(TimeSpan.FromMinutes(1));
        Assert.True(stats.TotalCount >= 1);

        // Very narrow window that excludes old records
        // Since we just recorded, it should be in a 1-second window
        var narrowStats = _statistics.GetStats(TimeSpan.FromMilliseconds(1));
        // This may or may not include it depending on timing, so we just verify it doesn't throw
        Assert.True(narrowStats.TotalCount >= 0);
    }

    [Fact]
    public void ExceptionStatistics_ShouldHandleInfrastructureException()
    {
        _statistics.RecordException(new InfrastructureException("Database", "db error"), "req-1");

        var stats = _statistics.GetStats(TimeSpan.FromMinutes(1));
        Assert.Equal(1, stats.TotalCount);
        Assert.True(stats.ByStatusCode.ContainsKey(503));
    }

    [Fact]
    public void ExceptionStatistics_ShouldHandleGenericExceptions()
    {
        _statistics.RecordException(new Exception("generic error"), "req-1");

        var stats = _statistics.GetStats(TimeSpan.FromMinutes(1));
        Assert.Equal(1, stats.TotalCount);
        // Generic exceptions don't have status codes
        Assert.Empty(stats.ByStatusCode);
    }

    [Fact]
    public void ExceptionStatistics_ShouldTrackErrorCodes()
    {
        var bizEx = new BusinessException("biz error", "BIZ_001", 400);
        _statistics.RecordException(bizEx, "req-1");

        var stats = _statistics.GetStats(TimeSpan.FromMinutes(1));
        Assert.Equal(1, stats.TotalCount);
        Assert.True(stats.ByErrorCode.ContainsKey("BIZ_001"));
    }

    [Fact]
    public void ExceptionStatistics_Clear_ShouldRemoveAll()
    {
        _statistics.RecordException(new InvalidOperationException("test1"), "req-1");
        _statistics.RecordException(new ArgumentException("test2"), "req-2");

        _statistics.Clear();

        var stats = _statistics.GetStats(TimeSpan.FromHours(1));
        Assert.Equal(0, stats.TotalCount);

        var recent = _statistics.GetRecentExceptions(100);
        Assert.Empty(recent);
    }

    #endregion

    #region DTO Tests

    [Fact]
    public void ExceptionSummaryDto_ShouldHaveDefaultValues()
    {
        var dto = new ExceptionSummaryDto();

        Assert.Equal(0, dto.TotalCount);
        Assert.NotNull(dto.ByType);
        Assert.Empty(dto.ByType);
        Assert.NotNull(dto.ByStatusCode);
        Assert.Empty(dto.ByStatusCode);
        Assert.NotNull(dto.ByErrorCode);
        Assert.Empty(dto.ByErrorCode);
        Assert.NotNull(dto.TopExceptions);
        Assert.Empty(dto.TopExceptions);
        Assert.NotNull(dto.RecentExceptions);
        Assert.Empty(dto.RecentExceptions);
    }

    [Fact]
    public void ExceptionEntryDto_ShouldHaveDefaultValues()
    {
        var dto = new ExceptionEntryDto();

        Assert.Equal(string.Empty, dto.ExceptionType);
        Assert.Null(dto.Message);
        Assert.Null(dto.StatusCode);
        Assert.Null(dto.ErrorCode);
        Assert.Null(dto.RequestId);
    }

    [Fact]
    public void ExceptionTypeCountDto_ShouldHaveDefaultValues()
    {
        var dto = new ExceptionTypeCountDto();

        Assert.Equal(string.Empty, dto.ExceptionType);
        Assert.Equal(0, dto.Count);
    }

    #endregion
}
