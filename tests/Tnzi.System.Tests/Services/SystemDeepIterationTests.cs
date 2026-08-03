namespace Tnzi.System.Tests.Services;

/// <summary>
/// AccessLogService E1 增强测试 - GetAccessLogTrendAsync + GetTopEndpointsAsync
/// </summary>
public class AccessLogTrendTests
{
    private readonly Mock<IRepository<AccessLog, Guid>> _accessLogRepositoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IAccessLogSender> _accessLogSenderMock;
    private readonly AccessLogService _service;

    public AccessLogTrendTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _accessLogRepositoryMock = new Mock<IRepository<AccessLog, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _accessLogSenderMock = new Mock<IAccessLogSender>();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _accessLogSenderMock.Setup(x => x.SendAsync(It.IsAny<AccessLogDto>())).Returns(Task.CompletedTask);

        _service = new AccessLogService(
            _serviceProviderMock.Object,
            _accessLogRepositoryMock.Object,
            _accessLogSenderMock.Object);
    }

    /// <summary>
    /// 设置 AccessLog 仓储的 IQueryable mock（支持 AsQueryable + AsNoTracking + async LINQ）
    /// </summary>
    private void SetupAccessLogQueryable(List<AccessLog> logs)
    {
        var mockQueryable = logs.BuildMock();
        _accessLogRepositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        _accessLogRepositoryMock.As<IQueryable<AccessLog>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _accessLogRepositoryMock.As<IQueryable<AccessLog>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _accessLogRepositoryMock.As<IQueryable<AccessLog>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _accessLogRepositoryMock.As<IQueryable<AccessLog>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    #region GetAccessLogTrendAsync

    [Fact]
    public async Task GetAccessLogTrendAsync_Daily_Should_Return_Correct_DataPoint_Count()
    {
        // Arrange: 3 天范围内 5 条日志
        var startDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 4, 0, 0, 0, DateTimeKind.Utc); // 3 天

        var logs = new List<AccessLog>
        {
            CreateAccessLog(startDate.AddHours(1), 200, 50, Guid.NewGuid()),
            CreateAccessLog(startDate.AddHours(5), 201, 120, Guid.NewGuid()),
            CreateAccessLog(startDate.AddDays(1).AddHours(2), 404, 30, Guid.NewGuid()),
            CreateAccessLog(startDate.AddDays(2).AddHours(3), 500, 200, null),
            CreateAccessLog(startDate.AddDays(2).AddHours(8), 200, 80, Guid.NewGuid())
        };

        SetupAccessLogQueryable(logs);

        // Act
        var result = await _service.GetAccessLogTrendAsync(
            TrendInterval.Daily, startDate, endDate);

        // Assert: 3 天应该返回 3 个数据点
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.DataPoints.Count.ShouldBe(3);
        result.Data.Interval.ShouldBe(TrendInterval.Daily);
        result.Data.StartDate.ShouldBe(startDate);
        result.Data.EndDate.ShouldBe(endDate);
    }

    [Fact]
    public async Task GetAccessLogTrendAsync_Daily_DataPoints_Have_Correct_Labels()
    {
        // Arrange
        var startDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc);

        SetupAccessLogQueryable(new List<AccessLog>());

        // Act
        var result = await _service.GetAccessLogTrendAsync(
            TrendInterval.Daily, startDate, endDate);

        // Assert: 每个数据点的标签应为日期格式 yyyy-MM-dd
        result.Data.ShouldNotBeNull();
        result.Data.DataPoints[0].Label.ShouldBe("2026-03-10");
        result.Data.DataPoints[1].Label.ShouldBe("2026-03-11");
        result.Data.DataPoints[2].Label.ShouldBe("2026-03-12");
    }

    [Fact]
    public async Task GetAccessLogTrendAsync_Weekly_Should_Group_By_Week()
    {
        // Arrange: 约 3 周的范围
        var startDate = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc); // 周一
        var endDate = new DateTime(2026, 2, 23, 0, 0, 0, DateTimeKind.Utc); // 3 周后

        var logs = new List<AccessLog>
        {
            CreateAccessLog(startDate.AddDays(1), 200, 100, Guid.NewGuid()),  // 第 1 周
            CreateAccessLog(startDate.AddDays(8), 200, 50, Guid.NewGuid()),   // 第 2 周
            CreateAccessLog(startDate.AddDays(15), 200, 80, Guid.NewGuid())   // 第 3 周
        };

        SetupAccessLogQueryable(logs);

        // Act
        var result = await _service.GetAccessLogTrendAsync(
            TrendInterval.Weekly, startDate, endDate);

        // Assert: 3 周的数据
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.DataPoints.Count.ShouldBe(3);
        result.Data.Interval.ShouldBe(TrendInterval.Weekly);

        // 每个周桶应该有 1 条日志
        foreach (var dp in result.Data.DataPoints)
        {
            dp.TotalRequests.ShouldBe(1);
        }
    }

    [Fact]
    public async Task GetAccessLogTrendAsync_Monthly_Should_Group_By_Month()
    {
        // Arrange: 3 个月范围
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var logs = new List<AccessLog>
        {
            CreateAccessLog(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), 200, 100, Guid.NewGuid()),
            CreateAccessLog(new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), 200, 50, Guid.NewGuid()),
            CreateAccessLog(new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc), 500, 300, null),
            CreateAccessLog(new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc), 404, 80, Guid.NewGuid())
        };

        SetupAccessLogQueryable(logs);

        // Act
        var result = await _service.GetAccessLogTrendAsync(
            TrendInterval.Monthly, startDate, endDate);

        // Assert: 3 个月 → 3 个数据点
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.DataPoints.Count.ShouldBe(3);
        result.Data.DataPoints[0].Label.ShouldBe("2026-01");
        result.Data.DataPoints[1].Label.ShouldBe("2026-02");
        result.Data.DataPoints[2].Label.ShouldBe("2026-03");

        // 1 月: 1 条
        result.Data.DataPoints[0].TotalRequests.ShouldBe(1);
        // 2 月: 2 条
        result.Data.DataPoints[1].TotalRequests.ShouldBe(2);
        // 3 月: 1 条
        result.Data.DataPoints[2].TotalRequests.ShouldBe(1);
    }

    [Fact]
    public async Task GetAccessLogTrendAsync_EmptyDateRange_Should_Return_Empty_DataPoints()
    {
        // Arrange: startDate == endDate → 没有时间桶
        var date = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);

        SetupAccessLogQueryable(new List<AccessLog>());

        // Act
        var result = await _service.GetAccessLogTrendAsync(
            TrendInterval.Daily, date, date);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.DataPoints.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAccessLogTrendAsync_DataPoints_Contain_Correct_AggregateValues()
    {
        // Arrange: 在同一天放入不同状态的日志
        var startDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);

        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        var logs = new List<AccessLog>
        {
            CreateAccessLog(startDate.AddHours(1), 200, 100, userId1),  // 成功
            CreateAccessLog(startDate.AddHours(2), 201, 150, userId1),  // 成功 (同用户)
            CreateAccessLog(startDate.AddHours(3), 404, 50, userId2),   // 错误 (4xx)
            CreateAccessLog(startDate.AddHours(4), 500, 500, null),     // 错误 (5xx), 无用户
            CreateAccessLog(startDate.AddHours(5), 302, 30, userId2)    // 非 2xx 但不是错误 (3xx)
        };

        SetupAccessLogQueryable(logs);

        // Act
        var result = await _service.GetAccessLogTrendAsync(
            TrendInterval.Daily, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.DataPoints.Count.ShouldBe(1);

        var dp = result.Data.DataPoints[0];
        dp.TotalRequests.ShouldBe(5);
        dp.SuccessRequests.ShouldBe(2);  // 200 和 201
        dp.ErrorRequests.ShouldBe(2);     // 404 和 500
        dp.UniqueUsers.ShouldBe(2);       // userId1 和 userId2 (null 不算)
        dp.AverageResponseTime.ShouldBe(166.0, 1.0); // (100+150+50+500+30)/5 = 166
        dp.StartTime.ShouldBe(startDate);
        dp.Label.ShouldBe("2026-02-01");
    }

    [Fact]
    public async Task GetAccessLogTrendAsync_NoLogs_Should_Return_ZeroAggregates()
    {
        // Arrange: 有时间范围但无日志
        var startDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);

        SetupAccessLogQueryable(new List<AccessLog>());

        // Act
        var result = await _service.GetAccessLogTrendAsync(
            TrendInterval.Daily, startDate, endDate);

        // Assert: 2 个空数据点
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.DataPoints.Count.ShouldBe(2);

        foreach (var dp in result.Data.DataPoints)
        {
            dp.TotalRequests.ShouldBe(0);
            dp.SuccessRequests.ShouldBe(0);
            dp.ErrorRequests.ShouldBe(0);
            dp.UniqueUsers.ShouldBe(0);
            dp.AverageResponseTime.ShouldBe(0);
        }
    }

    #endregion

    #region GetTopEndpointsAsync - DTO 与枚举契约测试

    [Fact]
    public void TopEndpointDto_Should_Have_All_Required_Properties()
    {
        // Arrange & Act
        var dto = new TopEndpointDto
        {
            Path = "/api/users",
            Method = "GET",
            TotalHits = 100,
            SuccessHits = 90,
            ErrorHits = 10,
            AverageResponseTime = 123.4,
            MaxResponseTime = 500,
            ErrorRate = 0.1
        };

        // Assert
        dto.Path.ShouldBe("/api/users");
        dto.Method.ShouldBe("GET");
        dto.TotalHits.ShouldBe(100);
        dto.SuccessHits.ShouldBe(90);
        dto.ErrorHits.ShouldBe(10);
        dto.AverageResponseTime.ShouldBe(123.4);
        dto.MaxResponseTime.ShouldBe(500);
        dto.ErrorRate.ShouldBe(0.1);
    }

    [Fact]
    public void TopEndpointSortBy_Enum_Should_Have_Expected_Values()
    {
        // Assert: 确认枚举存在所有预期值
        TopEndpointSortBy.Hits.ShouldBe((TopEndpointSortBy)0);
        TopEndpointSortBy.AverageResponseTime.ShouldBe((TopEndpointSortBy)1);
        TopEndpointSortBy.Errors.ShouldBe((TopEndpointSortBy)2);
    }

    [Fact]
    public void TopEndpointDto_ErrorRate_Should_Calculate_Correctly()
    {
        // Arrange: 模拟 GetTopEndpointsAsync 的错误率计算逻辑
        var dto = new TopEndpointDto
        {
            TotalHits = 200,
            ErrorHits = 50
        };

        // 服务层的计算逻辑: item.ErrorRate = (double)item.ErrorHits / item.TotalHits
        dto.ErrorRate = dto.TotalHits > 0 ? (double)dto.ErrorHits / dto.TotalHits : 0;

        // Assert
        dto.ErrorRate.ShouldBe(0.25);
    }

    [Fact]
    public void TopEndpointDto_ErrorRate_Should_Be_Zero_When_NoHits()
    {
        // Arrange
        var dto = new TopEndpointDto { TotalHits = 0, ErrorHits = 0 };
        dto.ErrorRate = dto.TotalHits > 0 ? (double)dto.ErrorHits / dto.TotalHits : 0;

        // Assert
        dto.ErrorRate.ShouldBe(0);
    }

    #endregion

    #region AccessLogTrendDto 契约测试

    [Fact]
    public void TrendInterval_Enum_Should_Have_Expected_Values()
    {
        TrendInterval.Daily.ShouldBe((TrendInterval)0);
        TrendInterval.Weekly.ShouldBe((TrendInterval)1);
        TrendInterval.Monthly.ShouldBe((TrendInterval)2);
    }

    [Fact]
    public void AccessLogTrendDataPoint_Should_Have_All_Required_Properties()
    {
        var dp = new AccessLogTrendDataPoint
        {
            Label = "2026-02-28",
            StartTime = new DateTime(2026, 2, 28),
            TotalRequests = 50,
            SuccessRequests = 40,
            ErrorRequests = 10,
            UniqueUsers = 5,
            AverageResponseTime = 200.5
        };

        dp.Label.ShouldBe("2026-02-28");
        dp.TotalRequests.ShouldBe(50);
        dp.SuccessRequests.ShouldBe(40);
        dp.ErrorRequests.ShouldBe(10);
        dp.UniqueUsers.ShouldBe(5);
        dp.AverageResponseTime.ShouldBe(200.5);
    }

    [Fact]
    public void AccessLogTrendDto_DataPoints_Default_Should_Be_Empty()
    {
        var dto = new AccessLogTrendDto();
        dto.DataPoints.ShouldNotBeNull();
        dto.DataPoints.ShouldBeEmpty();
    }

    #endregion

    #region Helper

    private static AccessLog CreateAccessLog(DateTime creationTime, int statusCode, long responseTime, Guid? userId)
    {
        return new AccessLog
        {
            Id = Guid.NewGuid(),
            CreationTime = creationTime,
            StatusCode = statusCode,
            ResponseTime = responseTime,
            UserId = userId,
            Path = "/api/test",
            Method = "GET"
        };
    }

    #endregion
}

/// <summary>
/// SettingService E1 增强测试 - GetSettingGroupsAsync
/// </summary>
public class SettingGroupTests
{
    private readonly Mock<IRepository<Setting, Guid>> _settingRepositoryMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<IOptionsMonitor<ApplicationOptions>> _applicationOptionsMock;
    private readonly Mock<ICache> _cacheMock;
    private readonly SettingService _service;

    public SettingGroupTests()
    {
        // Initialize Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _settingRepositoryMock = new Mock<IRepository<Setting, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _applicationOptionsMock = new Mock<IOptionsMonitor<ApplicationOptions>>();
        _applicationOptionsMock.SetupGet(x => x.CurrentValue).Returns(new ApplicationOptions { AppName = "TestApp", SiteName = "TestSite" });
        _cacheMock = new Mock<ICache>();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        var encryptionOptions = Microsoft.Extensions.Options.Options.Create(new SettingEncryptionOptions());

        _service = new SettingService(
            _serviceProviderMock.Object,
            _settingRepositoryMock.Object,
            _applicationOptionsMock.Object,
            encryptionOptions,
            _cacheMock.Object,
            Enumerable.Empty<ISettingProvider>(),
            Enumerable.Empty<ISettingDefinitionProvider>());
    }

    #region GetSettingGroupsAsync - DTO 契约测试

    [Fact]
    public void SettingGroupDto_Should_Have_All_Required_Properties()
    {
        // Arrange & Act
        var dto = new SettingGroupDto
        {
            GroupName = "General",
            SettingCount = 5
        };

        // Assert
        dto.GroupName.ShouldBe("General");
        dto.SettingCount.ShouldBe(5);
    }

    [Fact]
    public void SettingGroupDto_Default_Values_Should_Be_Correct()
    {
        // Arrange & Act
        var dto = new SettingGroupDto();

        // Assert
        dto.GroupName.ShouldBe(string.Empty);
        dto.SettingCount.ShouldBe(0);
    }

    [Fact]
    public void SettingGroupDto_Should_Support_Multiple_Groups()
    {
        // Arrange: 模拟 GetSettingGroupsAsync 返回的分组列表
        var groups = new List<SettingGroupDto>
        {
            new SettingGroupDto { GroupName = "General", SettingCount = 3 },
            new SettingGroupDto { GroupName = "Security", SettingCount = 5 },
            new SettingGroupDto { GroupName = "Email", SettingCount = 2 }
        };

        // Assert
        groups.Count.ShouldBe(3);
        groups.Sum(g => g.SettingCount).ShouldBe(10);
        groups.ShouldContain(g => g.GroupName == "General");
        groups.ShouldContain(g => g.GroupName == "Security");
        groups.ShouldContain(g => g.GroupName == "Email");
    }

    #endregion

    #region GetSettingGroupsAsync - 接口默认实现测试

    [Fact]
    public async Task ISettingService_GetSettingGroupsAsync_Default_Should_Return_501()
    {
        // Arrange: 直接调用接口默认实现 (模拟未覆盖默认方法的场景)
        var mockService = new Mock<ISettingService>();
        mockService.Setup(s => s.GetSettingGroupsAsync(It.IsAny<CancellationToken>()))
            .CallBase(); // 调用接口默认实现

        // Act
        var result = await mockService.Object.GetSettingGroupsAsync();

        // Assert: 接口默认实现返回 501 Not Implemented
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
    }

    #endregion
}
