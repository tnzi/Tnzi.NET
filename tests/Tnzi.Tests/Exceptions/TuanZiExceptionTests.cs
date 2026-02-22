
namespace Tnzi.Tests.Exceptions;

public class TnziExceptionTests
{
    [Fact]
    public void TnziException_ShouldSetCodeAndMessage()
    {
        // Arrange & Act
        var ex = new TnziException("TEST_CODE", "Test message");

        // Assert
        Assert.Equal("TEST_CODE", ex.Code);
        Assert.Equal("Test message", ex.Message);
    }

    [Fact]
    public void TnziException_WithData_ShouldAddContextData()
    {
        // Arrange & Act
        var ex = new TnziException("TEST_CODE", "Test message")
            .WithData("UserId", "123")
            .WithData("Action", "Login");

        // Assert
        Assert.NotNull(ex.ContextData);
        Assert.Equal(2, ex.ContextData.Count);
        Assert.Equal("123", ex.ContextData["UserId"]);
        Assert.Equal("Login", ex.ContextData["Action"]);
    }

    [Fact]
    public void BusinessException_ShouldHaveDefaultCode()
    {
        // Arrange & Act
        var ex = new BusinessException("Business error occurred");

        // Assert
        Assert.Equal("BUSINESS_ERROR", ex.Code);
        Assert.Equal("Business error occurred", ex.Message);
        Assert.Equal(400, ex.HttpStatusCode);
    }

    [Fact]
    public void EntityNotFoundException_ShouldContainEntityInfo()
    {
        // Arrange & Act
        var ex = new EntityNotFoundException("TestEntity", 123);

        // Assert
        Assert.Equal("RESOURCE_NOT_FOUND", ex.Code);
        Assert.Equal("TestEntity", ex.EntityTypeName);
        Assert.Equal(123, ex.EntityId);
        Assert.Contains("TestEntity", ex.Message);
    }

    [Fact]
    public void EntityNotFoundException_Generic_ShouldWork()
    {
        // Arrange & Act
        var ex = new EntityNotFoundException<TestEntity>(456);

        // Assert
        Assert.Equal("TestEntity", ex.EntityTypeName);
        Assert.Equal(456, ex.EntityId);
        Assert.Equal(404, ex.HttpStatusCode);
    }

    [Fact]
    public void ConfigurationException_ShouldContainConfigKey()
    {
        // Arrange & Act
        var ex = new ConfigurationException("Database:ConnectionString", "Connection string is missing");

        // Assert
        Assert.Equal("CONFIGURATION_ERROR", ex.Code);
        Assert.Equal("Configuration", ex.ComponentName);
        Assert.Equal("Database:ConnectionString", ex.ConfigurationKey);
        Assert.False(ex.IsRetryable);  // 配置错误不可重试
    }

    [Fact]
    public void ValidationException_ShouldContainErrors()
    {
        // Arrange
        var errors = new[]
        {
            new ValidationError("Email", "Invalid email format"),
            new ValidationError("Password", "Password too short", "123")
        };

        // Act
        var ex = new ValidationException(errors);

        // Assert
        Assert.Equal("VALIDATION_ERROR", ex.Code);
        Assert.NotNull(ex.ValidationErrors);
        Assert.Equal(2, ex.ValidationErrors.Count);
        Assert.Contains("Email", ex.ValidationErrors.Keys);
        Assert.Contains("Password", ex.ValidationErrors.Keys);
    }

    [Fact]
    public void AuthorizationException_ShouldContainPermission()
    {
        // Arrange & Act
        var ex = new AuthorizationException("Access denied", "admin.users.delete");

        // Assert
        Assert.Equal("FORBIDDEN", ex.Code);
        Assert.Equal("admin.users.delete", ex.RequiredPermission);
        Assert.Equal(403, ex.HttpStatusCode);
    }

    [Fact]
    public void UnauthorizedException_ShouldHave401StatusCode()
    {
        // Arrange & Act
        var ex = new UnauthorizedException();

        // Assert
        Assert.Equal("UNAUTHORIZED", ex.Code);
        Assert.Equal(401, ex.HttpStatusCode);
    }

    [Fact]
    public void ForbiddenException_ShouldHave403StatusCode()
    {
        // Arrange & Act
        var ex = new ForbiddenException("No permission to access this resource");

        // Assert
        Assert.Equal("FORBIDDEN", ex.Code);
        Assert.Equal(403, ex.HttpStatusCode);
    }

    [Fact]
    public void ConflictException_ShouldHave409StatusCode()
    {
        // Arrange & Act
        var ex = new ConflictException("Resource already exists");

        // Assert
        Assert.Equal("DATA_CONFLICT", ex.Code);
        Assert.Equal(409, ex.HttpStatusCode);
    }

    [Fact]
    public void ServiceUnavailableException_ShouldHave503StatusCode()
    {
        // Arrange & Act
        var ex = new ServiceUnavailableException();

        // Assert
        Assert.Equal("SERVICE_UNAVAILABLE", ex.Code);
        Assert.Equal(503, ex.HttpStatusCode);
    }

    [Fact]
    public void InfrastructureException_ShouldSetComponentAndRetryable()
    {
        // Arrange & Act
        var ex = new InfrastructureException("Database", "Connection failed", true);

        // Assert
        Assert.Equal("Database", ex.ComponentName);
        Assert.True(ex.IsRetryable);
        Assert.Equal("DATABASE_ERROR", ex.Code);
    }

    [Fact]
    public void InfrastructureException_ShouldSupportContextData()
    {
        // Arrange & Act
        var ex = new InfrastructureException("Cache", "Read failed", true)
            .WithData("CacheKey", "user:123");

        // Assert
        Assert.NotNull(ex.ContextData);
        Assert.Equal("user:123", ex.ContextData["CacheKey"]);
    }

    // Test fixture
    private class TestEntity { }
}