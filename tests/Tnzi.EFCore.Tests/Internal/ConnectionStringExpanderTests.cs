
namespace Tnzi.EFCore.Tests.Internal;

/// <summary>
/// ConnectionStringExpander 测试类
/// </summary>
public class ConnectionStringExpanderTests
{
    [Fact]
    public void Expand_WithEnvironmentVariable_ReplacesPlaceholder()
    {
        // Arrange
        var variableName = "TEST_DB_HOST";
        var variableValue = "test-host";
        Environment.SetEnvironmentVariable(variableName, variableValue);
        var connectionString = $"Host=${{{variableName}}};Port=5432;Database=mydb";

        try
        {
            // Act
            var result = ConnectionStringExpander.Expand(connectionString);

            // Assert
            Assert.Equal($"Host={variableValue};Port=5432;Database=mydb", result);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Expand_WithDollarParenSyntax_ReplacesPlaceholder()
    {
        // Arrange
        var variableName = "TEST_DB_PORT";
        var variableValue = "3306";
        Environment.SetEnvironmentVariable(variableName, variableValue);
        var connectionString = $"Host=localhost;Port=$(TEST_DB_PORT);Database=mydb";

        try
        {
            // Act
            var result = ConnectionStringExpander.Expand(connectionString);

            // Assert
            Assert.Equal($"Host=localhost;Port={variableValue};Database=mydb", result);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Expand_WithConfiguration_ReplacesPlaceholder()
    {
        // Arrange
        var configKey = "Database:Host";
        var configValue = "config-host";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [configKey] = configValue
            })
            .Build();

        var connectionString = $"Host=${{Database:Host}};Port=5432;Database=mydb";

        // Act
        var result = ConnectionStringExpander.Expand(connectionString, config);

        // Assert
        Assert.Equal($"Host={configValue};Port=5432;Database=mydb", result);
    }

    [Fact]
    public void Expand_WithUnderscoreSyntax_ReplacesPlaceholder()
    {
        // Arrange
        var configKey = "Database__Host";
        var configValue = "config-host-underscore";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [configKey] = configValue
            })
            .Build();

        var connectionString = $"Host=${{Database__Host}};Port=5432;Database=mydb";

        // Act
        var result = ConnectionStringExpander.Expand(connectionString, config);

        // Assert
        Assert.Equal($"Host={configValue};Port=5432;Database=mydb", result);
    }

    [Fact]
    public void Expand_WithUnresolvedPlaceholder_KeepsPlaceholder()
    {
        // Arrange
        var mockLogger = new Mock<ILogger>();
        var connectionString = "Host=${UNRESOLVED_VAR};Port=5432;Database=mydb";

        // Act
        var result = ConnectionStringExpander.Expand(connectionString, logger: mockLogger.Object);

        // Assert
        Assert.Equal(connectionString, result); // 占位符保持不变
    }

    [Fact]
    public void Expand_WithMultiplePlaceholders_ReplacesAll()
    {
        // Arrange
        Environment.SetEnvironmentVariable("DB_HOST", "host1");
        Environment.SetEnvironmentVariable("DB_PORT", "5432");
        var connectionString = "Host=${DB_HOST};Port=${DB_PORT};Database=mydb";

        try
        {
            // Act
            var result = ConnectionStringExpander.Expand(connectionString);

            // Assert
            Assert.Equal("Host=host1;Port=5432;Database=mydb", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_HOST", null);
            Environment.SetEnvironmentVariable("DB_PORT", null);
        }
    }

    [Fact]
    public void Expand_EmptyString_ReturnsEmptyString()
    {
        // Act
        var result = ConnectionStringExpander.Expand("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ContainsPlaceholders_WithPlaceholder_ReturnsTrue()
    {
        // Arrange
        var connectionString = "Host=${DB_HOST};Port=5432";

        // Act
        var result = ConnectionStringExpander.ContainsPlaceholders(connectionString);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ContainsPlaceholders_WithoutPlaceholder_ReturnsFalse()
    {
        // Arrange
        var connectionString = "Host=localhost;Port=5432;Database=mydb";

        // Act
        var result = ConnectionStringExpander.ContainsPlaceholders(connectionString);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Expand_EnvironmentVariableTakesPrecedence()
    {
        // Arrange
        var variableName = "TEST_VAR";
        var envValue = "env-value";
        var configValue = "config-value";
        Environment.SetEnvironmentVariable(variableName, envValue);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [variableName] = configValue
            })
            .Build();

        var connectionString = $"Value=${{{variableName}}}";

        try
        {
            // Act
            var result = ConnectionStringExpander.Expand(connectionString, config);

            // Assert
            Assert.Equal($"Value={envValue}", result); // 环境变量优先
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }
}