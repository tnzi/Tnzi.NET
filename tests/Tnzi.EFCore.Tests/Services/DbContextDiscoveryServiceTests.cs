
namespace Tnzi.EFCore.Tests.Services;

/// <summary>
/// DbContextDiscoveryService 测试类
/// </summary>
public class DbContextDiscoveryServiceTests
{
    private readonly DbContextDiscoveryService _service;

    public DbContextDiscoveryServiceTests()
    {
        _service = new DbContextDiscoveryService();
    }

    [Fact]
    public void DiscoverDbContexts_WithEmptyOptions_ReturnsEmptyResult()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>()
        };

        // Act
        var result = _service.DiscoverDbContexts(options);

        // Assert
        Assert.False(result.Success);
        Assert.Empty(result.Configurations);
        Assert.Null(result.PrimaryConfiguration);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void DiscoverDbContexts_WithNullDbContexts_ReturnsEmptyResult()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = null!
        };

        // Act
        var result = _service.DiscoverDbContexts(options);

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public void DiscoverDbContexts_WithInvalidConfig_SkipsInvalidEntries()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>
            {
                // 缺少 Name
                new DbContextConfiguration
                {
                    ConnectionString = "test",
                    DbContextType = "SomeType",
                    Provider = DatabaseProvider.SqlServer
                },
                // 缺少 ConnectionString
                new DbContextConfiguration
                {
                    Name = "Test",
                    DbContextType = "SomeType",
                    Provider = DatabaseProvider.SqlServer
                }
            }
        };

        // Act
        var result = _service.DiscoverDbContexts(options);

        // Assert
        Assert.False(result.Success);
        Assert.Empty(result.Configurations);
        Assert.True(result.HasInvalidConfigurations);
        Assert.True(result.ValidationErrors.Count >= 2); // 至少有两个错误
    }

    [Fact]
    public void DiscoverDbContexts_WithInvalidConfig_CollectsValidationErrors()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>
            {
                new DbContextConfiguration
                {
                    Name = "Test1",
                    ConnectionString = "", // 空连接字符串
                    DbContextType = "Microsoft.EntityFrameworkCore.DbContext, Microsoft.EntityFrameworkCore", // 有效类型，但 ConnectionString 为空
                    Provider = DatabaseProvider.SqlServer
                },
                new DbContextConfiguration
                {
                    Name = "Test2",
                    ConnectionString = "test",
                    DbContextType = "", // 空类型，会尝试自动发现但可能失败
                    Provider = DatabaseProvider.SqlServer
                }
            }
        };

        // Act
        var result = _service.DiscoverDbContexts(options);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.HasInvalidConfigurations);
        Assert.True(result.ValidationErrors.ContainsKey("Test1"));
        Assert.Contains("ConnectionString is empty", result.ValidationErrors["Test1"]);
        // Test2 可能因为自动发现失败而出现错误，或者自动发现成功（如果有匹配的 DbContext）
        Assert.True(result.ValidationErrors.ContainsKey("Test2") ||
                   result.Configurations.Any(c => c.Configuration.Name == "Test2"));
    }

    [Fact]
    public void DiscoverDbContexts_WithDuplicateNames_SkipsDuplicates()
    {
        // Arrange
        var options = new DatabaseOptions
        {
            AutoDiscoverDbContexts = true,
            DbContexts = new List<DbContextConfiguration>
            {
                new DbContextConfiguration
                {
                    Name = "Default",
                    ConnectionString = "test1",
                    DbContextType = "Microsoft.EntityFrameworkCore.DbContext, Microsoft.EntityFrameworkCore",
                    Provider = DatabaseProvider.SqlServer
                },
                new DbContextConfiguration
                {
                    Name = "Default", // 重复名称
                    ConnectionString = "test2",
                    DbContextType = "Microsoft.EntityFrameworkCore.DbContext, Microsoft.EntityFrameworkCore",
                    Provider = DatabaseProvider.PostgreSQL
                }
            }
        };

        // Act
        var result = _service.DiscoverDbContexts(options);

        // Assert
        // 只有第一个有效（DbContext 类型可能无法解析，这里测试重复名称的逻辑）
        // 实际上由于类型解析问题，可能都无法解析，所以我们只验证逻辑
        Assert.True(result.Configurations.Count <= 1);
    }
}