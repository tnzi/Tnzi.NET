
namespace Tnzi.Tests.ScopedContext;

/// <summary>
/// 作用域上下文测试
/// </summary>
public class ScopedContextTests
{
    [Fact]
    public void ScopedContext_ShouldStoreAndRetrieveItems()
    {
        // Arrange
        var context = new TestScopedContext();

        // Act
        context.SetItem("key1", "value1");
        context.SetItem("key2", 42);
        context.SetItem("key3", true);

        // Assert
        Assert.Equal("value1", context.GetItem<string>("key1"));
        Assert.Equal(42, context.GetItem<int>("key2"));
        Assert.True(context.GetItem<bool>("key3"));
    }

    [Fact]
    public void ScopedContext_ShouldReturnDefaultForMissingItem()
    {
        // Arrange
        var context = new TestScopedContext();

        // Act
        var stringValue = context.GetItem<string>("missing");
        var intValue = context.GetItem<int>("missing");

        // Assert
        Assert.Null(stringValue);
        Assert.Equal(0, intValue);
    }

    [Fact]
    public void ScopedContext_ShouldStoreProperties()
    {
        // Arrange
        var context = new TestScopedContext();
        var auditOperation = new AuditOperationInfo
        {
            OperationType = "Create",
            EntityType = "User"
        };

        // Act
        context.CurrentUserId = "user123";
        context.CurrentUserName = "TestUser";
        context.CurrentUserRoles = new[] { "Admin", "User" };
        context.AuditOperation = auditOperation;
        context.ClientIpAddress = "127.0.0.1";
        context.UserAgent = "TestAgent";
        context.TraceId = "trace123";

        // Assert
        Assert.Equal("user123", context.CurrentUserId);
        Assert.Equal("TestUser", context.CurrentUserName);
        Assert.Equal(2, context.CurrentUserRoles.Count);
        Assert.Equal("Create", context.AuditOperation?.OperationType);
        Assert.Equal("127.0.0.1", context.ClientIpAddress);
        Assert.Equal("TestAgent", context.UserAgent);
        Assert.Equal("trace123", context.TraceId);
    }

    [Fact]
    public void ScopedContext_Items_ShouldBeAccessible()
    {
        // Arrange
        var context = new TestScopedContext();

        // Act
        context.SetItem("key1", "value1");
        context.Items["key2"] = "value2";

        // Assert
        Assert.Equal("value1", context.Items["key1"]);
        Assert.Equal("value2", context.Items["key2"]);
        Assert.Equal(2, context.Items.Count);
    }
}

/// <summary>
/// 测试用的作用域上下文实现
/// </summary>
public class TestScopedContext : Tnzi.ScopedContext.ScopedContext
{
}