
namespace Tnzi.AspNetCore.Tests.ScopedContext;

/// <summary>
/// HttpContext作用域上下文测试
/// </summary>
public class HttpContextScopedContextTests
{
    [Fact]
    public void HttpContextScopedContext_ShouldStoreInHttpContextItems()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        var context = new HttpContextScopedContext(httpContextAccessor.Object);

        // Act
        context.CurrentUserId = "user123";
        context.CurrentUserName = "TestUser";

        // Assert
        Assert.Equal("user123", httpContext.Items[ScopedContextItems.CurrentUserId]);
        Assert.Equal("TestUser", httpContext.Items[ScopedContextItems.CurrentUserName]);
        Assert.Equal("user123", context.CurrentUserId);
        Assert.Equal("TestUser", context.CurrentUserName);
    }

    [Fact]
    public void HttpContextScopedContext_ShouldReturnNullWhenHttpContextIsNull()
    {
        // Arrange
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var context = new HttpContextScopedContext(httpContextAccessor.Object);

        // Act
        context.CurrentUserId = "user123";
        var retrieved = context.CurrentUserId;

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void HttpContextScopedContext_ShouldStoreAndRetrieveItems()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        var context = new HttpContextScopedContext(httpContextAccessor.Object);

        // Act
        context.SetItem("key1", "value1");
        context.SetItem("key2", 42);

        // Assert
        Assert.Equal("value1", context.GetItem<string>("key1"));
        Assert.Equal(42, context.GetItem<int>("key2"));
        Assert.Equal("value1", httpContext.Items["key1"]);
        Assert.Equal(42, httpContext.Items["key2"]);
    }

    [Fact]
    public void HttpContextScopedContext_ShouldHandleValueTypes()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        var context = new HttpContextScopedContext(httpContextAccessor.Object);

        // Act
        context.SetItem("intValue", 42);
        context.SetItem("boolValue", true);

        // Assert
        Assert.Equal(42, context.GetItem<int>("intValue"));
        Assert.True(context.GetItem<bool>("boolValue"));
        Assert.Equal(0, context.GetItem<int>("missing")); // 默认值
    }

    [Fact]
    public void HttpContextScopedContext_ShouldRemoveItemWhenValueIsNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Items[ScopedContextItems.CurrentUserId] = "user123";
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        var context = new HttpContextScopedContext(httpContextAccessor.Object);

        // Act
        context.CurrentUserId = null;

        // Assert
        Assert.False(httpContext.Items.ContainsKey(ScopedContextItems.CurrentUserId));
        Assert.Null(context.CurrentUserId);
    }

    [Fact]
    public void HttpContextScopedContext_ShouldFallbackToBaseWhenHttpContextIsNull()
    {
        // Arrange
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var context = new HttpContextScopedContext(httpContextAccessor.Object);

        // Act
        context.SetItem("key1", "value1");
        var retrieved = context.GetItem<string>("key1");

        // Assert
        Assert.Equal("value1", retrieved); // 应该从base获取
    }

    [Fact]
    public void GetItem_ThroughInterface_ReadsHttpContextItems()
    {
        // 精确锁定 override（非 new）修复：中间件直接写入 HttpContext.Items（未经 SetItem，
        // 故基类 _items 无此键）。经 IScopedContext 接口调用 GetItem 时，若派生方法用 new
        // 会被静态派发回基类实现（只读 _items）从而绕过 HttpContext.Items 返回默认值；
        // override 后接口调用走派生实现，能读到 HttpContext.Items。
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Items["direct-key"] = "direct-value";
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
        IScopedContext context = new HttpContextScopedContext(httpContextAccessor.Object);

        // Act
        var value = context.GetItem<string>("direct-key");

        // Assert
        Assert.Equal("direct-value", value);
    }
}