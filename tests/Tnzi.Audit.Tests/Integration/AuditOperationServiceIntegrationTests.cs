
namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// AuditOperationService 集成测试
/// </summary>
public class AuditOperationServiceIntegrationTests : IntegrationTestBase
{
    private readonly IAuditOperationService _service;

    public AuditOperationServiceIntegrationTests()
    {
        _service = ServiceProvider.GetRequiredService<IAuditOperationService>();
    }

    #region GetUserOperationsAsync Tests

    [Fact]
    public async Task GetUserOperationsAsync_Should_Return_User_Operations()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "TestController.Action1",
                ResultType = AuditResultType.Success,
                CreationTime = DateTime.UtcNow.AddHours(-2),
                StartTime = DateTime.UtcNow.AddHours(-2),
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "TestController.Action2",
                ResultType = AuditResultType.Failed,
                CreationTime = DateTime.UtcNow.AddHours(-1),
                StartTime = DateTime.UtcNow.AddHours(-1),
                Elapsed = 200
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = otherUserId,
                FunctionName = "TestController.Action3",
                ResultType = AuditResultType.Success,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 150
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetUserOperationsAsync(userId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        var resultList = result.Data.ToList();
        resultList.Count.ShouldBe(2);
        resultList.All(o => o.UserId == userId).ShouldBeTrue();
        // 应该按创建时间降序排列
        resultList[0].CreationTime.ShouldBeGreaterThan(resultList[1].CreationTime);
    }

    [Fact]
    public async Task GetUserOperationsAsync_Should_Filter_By_DateRange()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action1",
                ResultType = AuditResultType.Success,
                CreationTime = now.AddDays(-5),
                StartTime = now.AddDays(-5),
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action2",
                ResultType = AuditResultType.Success,
                CreationTime = now.AddDays(-2),
                StartTime = now.AddDays(-2),
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action3",
                ResultType = AuditResultType.Success,
                CreationTime = now,
                StartTime = now,
                Elapsed = 100
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetUserOperationsAsync(
            userId, 
            startDate: now.AddDays(-3), 
            endDate: now.AddDays(-1));

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        var resultList = result.Data.ToList();
        resultList.Count.ShouldBe(1);
        resultList[0].FunctionName.ShouldBe("Action2");
    }

    [Fact]
    public async Task GetUserOperationsAsync_Should_Filter_By_ResultType()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action1",
                ResultType = AuditResultType.Success,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action2",
                ResultType = AuditResultType.Failed,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action3",
                ResultType = AuditResultType.Warning,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 100
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetUserOperationsAsync(userId, resultType: AuditResultType.Failed);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        var resultList = result.Data.ToList();
        resultList.Count.ShouldBe(1);
        resultList[0].ResultType.ShouldBe(AuditResultType.Failed);
    }

    #endregion

    #region GetOperationsAsync Tests

    [Fact]
    public async Task GetOperationsAsync_Should_Return_Paged_Results()
    {
        // Arrange
        var operations = Enumerable.Range(1, 25).Select(i => new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = $"Action{i}",
            ResultType = AuditResultType.Success,
            CreationTime = DateTime.UtcNow.AddMinutes(-i),
            StartTime = DateTime.UtcNow.AddMinutes(-i),
            Elapsed = 100
        }).ToList();

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var query = new AuditOperationQueryDto
        {
            PageIndex = 1,
            PageSize = 10
        };
        var result = await _service.GetOperationsAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(25);
        result.Data.Items.Count.ShouldBe(10);
        result.Data.PageIndex.ShouldBe(1);
        result.Data.PageSize.ShouldBe(10);
    }

    [Fact]
    public async Task GetOperationsAsync_Should_Filter_By_FunctionName()
    {
        // Arrange
        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "UserController.Login",
                ResultType = AuditResultType.Success,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "UserController.Logout",
                ResultType = AuditResultType.Success,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "ProductController.Create",
                ResultType = AuditResultType.Success,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 100
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var query = new AuditOperationQueryDto
        {
            FunctionName = "UserController",
            PageIndex = 1,
            PageSize = 10
        };
        var result = await _service.GetOperationsAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        var data = result.Data!;
        data.TotalCount.ShouldBe(2);
        data.Items.All(o => o.FunctionName != null && o.FunctionName.Contains("UserController")).ShouldBeTrue();
    }

    [Fact]
    public async Task GetOperationsAsync_Should_Filter_By_Multiple_Criteria()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "TestController.Action1",
                ResultType = AuditResultType.Success,
                Ip = "192.168.1.1",
                CreationTime = now.AddHours(-1),
                StartTime = now.AddHours(-1),
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "TestController.Action2",
                ResultType = AuditResultType.Failed,
                Ip = "192.168.1.1",
                CreationTime = now,
                StartTime = now,
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                FunctionName = "TestController.Action3",
                ResultType = AuditResultType.Failed,
                Ip = "192.168.1.2",
                CreationTime = now,
                StartTime = now,
                Elapsed = 100
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var query = new AuditOperationQueryDto
        {
            UserId = userId,
            ResultType = AuditResultType.Failed,
            Ip = "192.168.1.1",
            PageIndex = 1,
            PageSize = 10
        };
        var result = await _service.GetOperationsAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(1);
        result.Data.Items[0].FunctionName.ShouldBe("TestController.Action2");
    }

    #endregion

    #region GetFunctionStatisticsAsync Tests

    [Fact]
    public async Task GetFunctionStatisticsAsync_Should_Calculate_Statistics()
    {
        // Arrange
        var functionName = "TestController.TestAction";

        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = functionName,
                ResultType = AuditResultType.Success,
                Elapsed = 100,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = functionName,
                ResultType = AuditResultType.Success,
                Elapsed = 200,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = functionName,
                ResultType = AuditResultType.Failed,
                Elapsed = 150,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = functionName,
                ResultType = AuditResultType.Warning,
                Elapsed = 300,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetFunctionStatisticsAsync(functionName);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(4);
        result.Data.SuccessCount.ShouldBe(2);
        result.Data.FailedCount.ShouldBe(1);
        result.Data.WarningCount.ShouldBe(1);
        result.Data.AverageElapsed.ShouldBe(187.5); // (100+200+150+300)/4
        result.Data.MaxElapsed.ShouldBe(300);
        result.Data.MinElapsed.ShouldBe(100);
    }

    [Fact]
    public async Task GetFunctionStatisticsAsync_Should_Return_Empty_When_No_Data()
    {
        // Act
        var result = await _service.GetFunctionStatisticsAsync("NonExistentFunction");

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(0);
        result.Data!.SuccessCount.ShouldBe(0);
        result.Data!.FailedCount.ShouldBe(0);
        result.Data!.WarningCount.ShouldBe(0);
    }

    #endregion

    #region GetUserStatisticsAsync Tests

    [Fact]
    public async Task GetUserStatisticsAsync_Should_Calculate_User_Statistics()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action1",
                ResultType = AuditResultType.Success,
                Elapsed = 100,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action2",
                ResultType = AuditResultType.Failed,
                Elapsed = 200,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FunctionName = "Action3",
                ResultType = AuditResultType.Success,
                Elapsed = 150,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetUserStatisticsAsync(userId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(3);
        result.Data.SuccessCount.ShouldBe(2);
        result.Data.FailedCount.ShouldBe(1);
        result.Data.AverageElapsed.ShouldBe(150); // (100+200+150)/3
    }

    #endregion

    #region DeleteExpiredOperationsAsync Tests

    [Fact]
    public async Task DeleteExpiredOperationsAsync_Should_Delete_Old_Operations()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "OldAction",
                ResultType = AuditResultType.Success,
                CreationTime = now.AddDays(-100),
                StartTime = now.AddDays(-100),
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "RecentAction",
                ResultType = AuditResultType.Success,
                CreationTime = now.AddDays(-30),
                StartTime = now.AddDays(-30),
                Elapsed = 100
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _service.DeleteExpiredOperationsAsync(days: 90);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(1);
        
        var remainingOperations = DbContext.AuditOperations.ToList();
        remainingOperations.Count.ShouldBe(1);
        remainingOperations[0].FunctionName.ShouldBe("RecentAction");
    }

    [Fact]
    public async Task DeleteExpiredOperationsAsync_Should_Return_Zero_When_No_Expired()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var operation = new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = "RecentAction",
            ResultType = AuditResultType.Success,
            CreationTime = now.AddDays(-30),
            StartTime = now.AddDays(-30),
            Elapsed = 100
        };

        await DbContext.AuditOperations.AddAsync(operation);
        await DbContext.SaveChangesAsync();

        // Act
        var result = await _service.DeleteExpiredOperationsAsync(days: 90);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
        DbContext.AuditOperations.Count().ShouldBe(1);
    }

    #endregion
}
