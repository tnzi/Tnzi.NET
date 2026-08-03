
namespace Tnzi.AI.Tests;

/// <summary>
/// ToolContextAccessor 单元测试
/// </summary>
public class ToolContextAccessorTests
{
    [Fact]
    public void Current_DefaultIsNull()
    {
        ToolContextAccessor.Current.ShouldBeNull();
    }

    [Fact]
    public void Current_SetAndGet_ReturnsContext()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestToolContext(services);

        try
        {
            ToolContextAccessor.Current = context;
            ToolContextAccessor.Current.ShouldBe(context);
        }
        finally
        {
            ToolContextAccessor.Current = null;
        }
    }

    [Fact]
    public void Current_ClearReturnsNull()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        ToolContextAccessor.Current = new TestToolContext(services);
        ToolContextAccessor.Current.ShouldNotBeNull();

        ToolContextAccessor.Current = null;
        ToolContextAccessor.Current.ShouldBeNull();
    }

    [Fact]
    public async Task Current_FlowsAcrossAsyncContexts()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestToolContext(services);

        try
        {
            ToolContextAccessor.Current = context;

            // 验证异步上下文中可以访问
            var result = await Task.Run(() => ToolContextAccessor.Current);
            result.ShouldBe(context);
        }
        finally
        {
            ToolContextAccessor.Current = null;
        }
    }

    [Fact]
    public async Task Current_IsolatedBetweenAsyncFlows()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context1 = new TestToolContext(services);
        var context2 = new TestToolContext(services);

        var task1 = Task.Run(async () =>
        {
            ToolContextAccessor.Current = context1;
            await Task.Delay(50);
            return ToolContextAccessor.Current;
        });

        var task2 = Task.Run(async () =>
        {
            ToolContextAccessor.Current = context2;
            await Task.Delay(50);
            return ToolContextAccessor.Current;
        });

        var results = await Task.WhenAll(task1, task2);
        results[0].ShouldBe(context1);
        results[1].ShouldBe(context2);
    }

    [Fact]
    public void GetService_ResolvesFromRequestServices()
    {
        var services = new ServiceCollection()
            .AddSingleton<ITestService, TestService>()
            .BuildServiceProvider();

        var context = new TestToolContext(services);

        try
        {
            ToolContextAccessor.Current = context;
            var svc = ToolContextAccessor.Current!.GetService<ITestService>();
            svc.ShouldNotBeNull();
            svc.ShouldBeOfType<TestService>();
        }
        finally
        {
            ToolContextAccessor.Current = null;
        }
    }

    [Fact]
    public void GetRequiredService_ThrowsWhenNotRegistered()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestToolContext(services);

        try
        {
            ToolContextAccessor.Current = context;
            Should.Throw<InvalidOperationException>(() =>
                ToolContextAccessor.Current!.GetRequiredService<ITestService>());
        }
        finally
        {
            ToolContextAccessor.Current = null;
        }
    }

    [Fact]
    public void Items_StoresAndRetrievesData()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new TestToolContext(services);

        context.Items["key1"] = "value1";
        context.Items["key2"] = 42;

        context.Items["key1"].ShouldBe("value1");
        context.Items["key2"].ShouldBe(42);
    }

    // Test helpers
    private interface ITestService { }
    private class TestService : ITestService { }

    private class TestToolContext : IToolContext
    {
        private readonly IServiceProvider _services;

        public TestToolContext(IServiceProvider services) => _services = services;
        public IServiceProvider RequestServices => _services;
        public CancellationToken CancellationToken => CancellationToken.None;
        public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>();
        public T? GetService<T>() where T : class => _services.GetService<T>();
        public T GetRequiredService<T>() where T : notnull => _services.GetRequiredService<T>();
    }
}
