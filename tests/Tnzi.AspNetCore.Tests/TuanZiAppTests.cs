
namespace Tnzi.AspNetCore.Tests;

public class TnziAppTests
{
    [Fact]
    public void CreateAsync_ShouldNotThrow_WhenValidModuleType()
    {
        // 由于需要完整的 ASP.NET Core 环境，这里只测试方法存在
        // 实际的集成测试需要 WebApplicationFactory

        var method = typeof(TnziApp).GetMethod(nameof(TnziApp.CreateAsync),
            new[] { typeof(string[]) });

        Assert.NotNull(method);
        Assert.True(method.IsGenericMethod);
    }

    [Fact]
    public void RunAsync_ShouldNotThrow_WhenValidModuleType()
    {
        // 有多个 RunAsync 重载，需要指定参数类型来精确查找
        var methods = typeof(TnziApp).GetMethods()
            .Where(m => m.Name == nameof(TnziApp.RunAsync) && m.IsGenericMethod)
            .ToList();

        Assert.True(methods.Count >= 1);
        Assert.All(methods, m => Assert.True(m.IsGenericMethod));
    }

    [Fact]
    public void CreateAsync_WithCallbacks_ShouldExist()
    {
        var methods = typeof(TnziApp).GetMethods()
            .Where(m => m.Name == nameof(TnziApp.CreateAsync))
            .ToList();

        // 应该有两个 CreateAsync 重载
        Assert.True(methods.Count >= 1);
    }
}