
namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// 审计模块深度迭代测试 — 趋势统计、Top功能、Top用户
/// </summary>
public class AuditDeepIterationTests
{
    private readonly Mock<IRepository<AuditOperation, Guid>> _repositoryMock;
    private readonly Mock<IAuditStore> _auditStoreMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly AuditOperationService _service;

    public AuditDeepIterationTests()
    {
        _repositoryMock = new Mock<IRepository<AuditOperation, Guid>>();
        _auditStoreMock = new Mock<IAuditStore>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _service = new AuditOperationService(_repositoryMock.Object, _auditStoreMock.Object, _serviceProviderMock.Object);
    }

    private void SetupOperationQueryable(List<AuditOperation> operations)
    {
        var mock = operations.AsQueryable().BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable()).Returns(mock);

        // Setup Where() for IQueryable extension method
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.Provider).Returns(mock.Provider);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.Expression).Returns(mock.Expression);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.ElementType).Returns(mock.ElementType);
        _repositoryMock.As<IQueryable<AuditOperation>>().Setup(q => q.GetEnumerator()).Returns(mock.GetEnumerator());
    }

    #region GetAuditTrendAsync Tests

    [Fact]
    public async Task GetAuditTrendAsync_Daily_Should_Group_By_Day()
    {
        // Arrange
        var now = new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        var operations = new List<AuditOperation>
        {
            CreateOperation(now.AddDays(-2), AuditResultType.Success),
            CreateOperation(now.AddDays(-2).AddHours(3), AuditResultType.Failed),
            CreateOperation(now.AddDays(-1), AuditResultType.Success),
            CreateOperation(now.AddDays(-1).AddHours(5), AuditResultType.Success),
            CreateOperation(now.AddDays(-1).AddHours(8), AuditResultType.Warning),
            CreateOperation(now, AuditResultType.Success)
        };
        SetupOperationQueryable(operations);

        // Act
        var result = await _service.GetAuditTrendAsync(now.AddDays(-3), now.AddDays(1));

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(3); // 3 days with data
        result.Data[0].TotalCount.ShouldBe(2); // Day -2
        result.Data[0].SuccessCount.ShouldBe(1);
        result.Data[0].FailedCount.ShouldBe(1);
        result.Data[1].TotalCount.ShouldBe(3); // Day -1
        result.Data[1].SuccessCount.ShouldBe(2);
        result.Data[1].WarningCount.ShouldBe(1);
        result.Data[2].TotalCount.ShouldBe(1); // Today
    }

    [Fact]
    public async Task GetAuditTrendAsync_Monthly_Should_Group_By_Month()
    {
        // Arrange
        var operations = new List<AuditOperation>
        {
            CreateOperation(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), AuditResultType.Success),
            CreateOperation(new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), AuditResultType.Success),
            CreateOperation(new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), AuditResultType.Failed),
        };
        SetupOperationQueryable(operations);

        // Act
        var result = await _service.GetAuditTrendAsync(
            new DateTime(2026, 1, 1), new DateTime(2026, 3, 1), AuditTrendGroupBy.Monthly);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2); // Jan + Feb
        result.Data[0].Period.ShouldBe("2026-01");
        result.Data[0].TotalCount.ShouldBe(2);
        result.Data[1].Period.ShouldBe("2026-02");
        result.Data[1].FailedCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetAuditTrendAsync_InvalidDateRange_Should_Fail()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var result = await _service.GetAuditTrendAsync(now, now.AddDays(-1));

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetAuditTrendAsync_EmptyData_Should_Return_EmptyList()
    {
        // Arrange
        SetupOperationQueryable(new List<AuditOperation>());

        // Act
        var result = await _service.GetAuditTrendAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetAuditTrendAsync_AverageElapsed_Should_Be_Calculated()
    {
        // Arrange
        var now = new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc);
        var operations = new List<AuditOperation>
        {
            CreateOperation(now, AuditResultType.Success, elapsed: 100),
            CreateOperation(now.AddHours(1), AuditResultType.Success, elapsed: 300),
        };
        SetupOperationQueryable(operations);

        // Act
        var result = await _service.GetAuditTrendAsync(now.AddDays(-1), now.AddDays(1));

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data![0].AverageElapsed.ShouldBe(200.0);
    }

    #endregion

    #region GetTopFunctionsAsync Tests

    [Fact]
    public async Task GetTopFunctionsAsync_Should_Return_Top_Functions_By_HitCount()
    {
        // Arrange
        var operations = new List<AuditOperation>
        {
            CreateOperation(functionName: "User.Login", resultType: AuditResultType.Success, elapsed: 50),
            CreateOperation(functionName: "User.Login", resultType: AuditResultType.Success, elapsed: 60),
            CreateOperation(functionName: "User.Login", resultType: AuditResultType.Failed, elapsed: 100),
            CreateOperation(functionName: "Order.Create", resultType: AuditResultType.Success, elapsed: 200),
            CreateOperation(functionName: "Order.Create", resultType: AuditResultType.Success, elapsed: 150),
            CreateOperation(functionName: "Product.Get", resultType: AuditResultType.Success, elapsed: 30),
        };
        SetupOperationQueryable(operations);

        // Act
        var result = await _service.GetTopFunctionsAsync(topN: 2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data[0].FunctionName.ShouldBe("User.Login"); // 3 hits
        result.Data[0].HitCount.ShouldBe(3);
        result.Data[0].ErrorCount.ShouldBe(1);
        result.Data[0].ErrorRate.ShouldBeGreaterThan(0.3);
        result.Data[0].ErrorRate.ShouldBeLessThan(0.4);
        result.Data[1].FunctionName.ShouldBe("Order.Create"); // 2 hits
        result.Data[1].HitCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetTopFunctionsAsync_Should_Calculate_AverageElapsed()
    {
        // Arrange
        var operations = new List<AuditOperation>
        {
            CreateOperation(functionName: "Test.Action", elapsed: 100),
            CreateOperation(functionName: "Test.Action", elapsed: 200),
            CreateOperation(functionName: "Test.Action", elapsed: 300),
        };
        SetupOperationQueryable(operations);

        // Act
        var result = await _service.GetTopFunctionsAsync(topN: 1);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data![0].AverageElapsed.ShouldBe(200.0);
        result.Data[0].MaxElapsed.ShouldBe(300);
    }

    [Fact]
    public async Task GetTopFunctionsAsync_WithDateFilter_Should_Filter()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var operations = new List<AuditOperation>
        {
            CreateOperation(now.AddDays(-10), functionName: "Old.Action"),
            CreateOperation(now.AddDays(-1), functionName: "Recent.Action"),
        };
        SetupOperationQueryable(operations);

        // Act
        var result = await _service.GetTopFunctionsAsync(topN: 10, startDate: now.AddDays(-2));

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(1);
        result.Data[0].FunctionName.ShouldBe("Recent.Action");
    }

    [Fact]
    public async Task GetTopFunctionsAsync_InvalidTopN_Should_Fail()
    {
        // Act
        var result = await _service.GetTopFunctionsAsync(topN: 0);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetTopFunctionsAsync_EmptyData_Should_Return_EmptyList()
    {
        // Arrange
        SetupOperationQueryable(new List<AuditOperation>());

        // Act
        var result = await _service.GetTopFunctionsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(0);
    }

    #endregion

    #region GetTopUsersAsync Tests

    [Fact]
    public async Task GetTopUsersAsync_Should_Return_Top_Users_By_OperationCount()
    {
        // Arrange
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var operations = new List<AuditOperation>
        {
            CreateOperation(userId: user1, userName: "admin", resultType: AuditResultType.Success),
            CreateOperation(userId: user1, userName: "admin", resultType: AuditResultType.Success),
            CreateOperation(userId: user1, userName: "admin", resultType: AuditResultType.Failed),
            CreateOperation(userId: user2, userName: "user1", resultType: AuditResultType.Success),
        };
        SetupOperationQueryable(operations);

        // Act
        var result = await _service.GetTopUsersAsync(topN: 2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data[0].UserId.ShouldBe(user1);
        result.Data[0].OperationCount.ShouldBe(3);
        result.Data[0].SuccessCount.ShouldBe(2);
        result.Data[0].FailedCount.ShouldBe(1);
        result.Data[0].SuccessRate.ShouldBeInRange(0.66, 0.67);
        result.Data[1].UserId.ShouldBe(user2);
        result.Data[1].OperationCount.ShouldBe(1);
        result.Data[1].SuccessRate.ShouldBe(1.0);
    }

    [Fact]
    public async Task GetTopUsersAsync_Should_Exclude_Anonymous()
    {
        // Arrange - operations without UserId should be excluded
        var userId = Guid.NewGuid();
        var operations = new List<AuditOperation>
        {
            CreateOperation(userId: userId, userName: "admin"),
            CreateOperation(userId: null, userName: null), // anonymous
            CreateOperation(userId: null, userName: null), // anonymous
        };
        SetupOperationQueryable(operations);

        // Act
        var result = await _service.GetTopUsersAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(1);
        result.Data[0].UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task GetTopUsersAsync_InvalidTopN_Should_Fail()
    {
        // Act
        var result = await _service.GetTopUsersAsync(topN: -1);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetTopUsersAsync_EmptyData_Should_Return_EmptyList()
    {
        // Arrange
        SetupOperationQueryable(new List<AuditOperation>());

        // Act
        var result = await _service.GetTopUsersAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(0);
    }

    #endregion

    #region DTO Contract Tests

    [Fact]
    public void AuditTrendPointDto_Should_Have_All_Properties()
    {
        var dto = new AuditTrendPointDto
        {
            Period = "2026-02-28",
            TotalCount = 100,
            SuccessCount = 80,
            FailedCount = 15,
            WarningCount = 5,
            AverageElapsed = 150.5
        };

        dto.Period.ShouldBe("2026-02-28");
        dto.TotalCount.ShouldBe(100);
        dto.SuccessCount.ShouldBe(80);
        dto.FailedCount.ShouldBe(15);
        dto.WarningCount.ShouldBe(5);
        dto.AverageElapsed.ShouldBe(150.5);
    }

    [Fact]
    public void TopFunctionDto_Should_Have_All_Properties()
    {
        var dto = new TopFunctionDto
        {
            FunctionName = "Test.Action",
            HitCount = 50,
            AverageElapsed = 120.0,
            MaxElapsed = 500,
            ErrorCount = 5,
            ErrorRate = 0.1
        };

        dto.FunctionName.ShouldBe("Test.Action");
        dto.HitCount.ShouldBe(50);
        dto.AverageElapsed.ShouldBe(120.0);
        dto.MaxElapsed.ShouldBe(500);
        dto.ErrorCount.ShouldBe(5);
        dto.ErrorRate.ShouldBe(0.1);
    }

    [Fact]
    public void TopUserDto_Should_Have_All_Properties()
    {
        var userId = Guid.NewGuid();
        var dto = new TopUserDto
        {
            UserId = userId,
            UserName = "admin",
            OperationCount = 30,
            SuccessCount = 25,
            FailedCount = 5,
            SuccessRate = 0.833
        };

        dto.UserId.ShouldBe(userId);
        dto.UserName.ShouldBe("admin");
        dto.OperationCount.ShouldBe(30);
        dto.SuccessCount.ShouldBe(25);
        dto.FailedCount.ShouldBe(5);
        dto.SuccessRate.ShouldBe(0.833);
    }

    [Fact]
    public void AuditTrendGroupBy_Should_Have_Three_Values()
    {
        Enum.GetValues<AuditTrendGroupBy>().Length.ShouldBe(3);
        Enum.IsDefined(AuditTrendGroupBy.Daily).ShouldBeTrue();
        Enum.IsDefined(AuditTrendGroupBy.Weekly).ShouldBeTrue();
        Enum.IsDefined(AuditTrendGroupBy.Monthly).ShouldBeTrue();
    }

    #endregion

    #region Helpers

    private static AuditOperation CreateOperation(
        DateTime? creationTime = null,
        AuditResultType resultType = AuditResultType.Success,
        string functionName = "Test.Action",
        long elapsed = 50,
        Guid? userId = null,
        string? userName = null)
    {
        return new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = functionName,
            ResultType = resultType,
            Elapsed = elapsed,
            CreationTime = creationTime ?? DateTime.UtcNow,
            StartTime = creationTime ?? DateTime.UtcNow,
            UserId = userId,
            UserName = userName
        };
    }

    #endregion
}
