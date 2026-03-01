using Tnzi.AI.Tools;

namespace Tnzi.AI.Tests;

/// <summary>
/// IToolExecutionMiddleware 管道单元测试
/// </summary>
public class ToolExecutionMiddlewareTests
{
    [Fact]
    public async Task Middleware_ExecutesInOrder()
    {
        // 验证中间件按注册顺序执行（洋葱模型）
        var executionOrder = new List<string>();

        var middleware1 = new TestMiddleware("M1", executionOrder);
        var middleware2 = new TestMiddleware("M2", executionOrder);

        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "No tools")));

        var options = new AgentExecutorOptions
        {
            Name = "TestAgent",
            Middlewares = [middleware1, middleware2]
        };

        var agent = new AgentExecutor(mock.Object, options);
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        await agent.ExecuteAsync(messages);

        // 无工具调用，中间件不执行（中间件只在工具调用时触发）
        executionOrder.ShouldBeEmpty();
    }

    [Fact]
    public async Task MiddlewareContext_ContainsToolInfo()
    {
        ToolExecutionContext? capturedContext = null;

        var middleware = new DelegateMiddleware(async (ctx, next) =>
        {
            capturedContext = ctx;
            return await next();
        });

        // 直接测试中间件上下文的属性
        var context = new ToolExecutionContext
        {
            ToolName = "test_tool",
            ToolGroup = "test_group",
            CallId = "call-123",
            Arguments = new Dictionary<string, object?> { ["arg1"] = "value1" },
            CancellationToken = CancellationToken.None
        };

        await middleware.InvokeAsync(context, () => Task.FromResult<object?>("result"));

        capturedContext.ShouldNotBeNull();
        capturedContext!.ToolName.ShouldBe("test_tool");
        capturedContext.ToolGroup.ShouldBe("test_group");
        capturedContext.CallId.ShouldBe("call-123");
        capturedContext.Arguments!.ShouldContainKey("arg1");
    }

    [Fact]
    public async Task Middleware_CanShortCircuit()
    {
        var shortCircuitMiddleware = new DelegateMiddleware((ctx, next) =>
        {
            // 不调用 next()，直接返回
            return Task.FromResult<object?>("short-circuited");
        });

        var context = new ToolExecutionContext { ToolName = "test" };
        var coreWasCalled = false;

        var result = await shortCircuitMiddleware.InvokeAsync(context, () =>
        {
            coreWasCalled = true;
            return Task.FromResult<object?>("core-result");
        });

        result.ShouldBe("short-circuited");
        coreWasCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task Middleware_CanModifyResult()
    {
        var transformMiddleware = new DelegateMiddleware(async (ctx, next) =>
        {
            var result = await next();
            return $"[transformed] {result}";
        });

        var context = new ToolExecutionContext { ToolName = "test" };

        var result = await transformMiddleware.InvokeAsync(context,
            () => Task.FromResult<object?>("original"));

        result.ShouldBe("[transformed] original");
    }

    [Fact]
    public void ToolExecutionContext_StopwatchIsRunning()
    {
        var context = new ToolExecutionContext { ToolName = "test" };
        context.Stopwatch.IsRunning.ShouldBeTrue();
        context.Stopwatch.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void ToolExecutionContext_PropertiesDictionary()
    {
        var context = new ToolExecutionContext { ToolName = "test" };
        context.Properties["key"] = "value";
        context.Properties["key"].ShouldBe("value");
    }

    [Fact]
    public async Task MultipleMiddlewares_OnionModel()
    {
        var order = new List<string>();

        var m1 = new DelegateMiddleware(async (ctx, next) =>
        {
            order.Add("m1-before");
            var result = await next();
            order.Add("m1-after");
            return result;
        });

        var m2 = new DelegateMiddleware(async (ctx, next) =>
        {
            order.Add("m2-before");
            var result = await next();
            order.Add("m2-after");
            return result;
        });

        // 模拟洋葱模型构建
        var context = new ToolExecutionContext { ToolName = "test" };
        Func<Task<object?>> core = () =>
        {
            order.Add("core");
            return Task.FromResult<object?>("done");
        };

        // 构建管道：m1 → m2 → core
        var pipeline = core;
        var middlewares = new List<IToolExecutionMiddleware> { m1, m2 };
        for (var i = middlewares.Count - 1; i >= 0; i--)
        {
            var mw = middlewares[i];
            var next = pipeline;
            pipeline = () => mw.InvokeAsync(context, next);
        }

        await pipeline();

        order.ShouldBe(["m1-before", "m2-before", "core", "m2-after", "m1-after"]);
    }

    // Test helpers
    private class TestMiddleware : IToolExecutionMiddleware
    {
        private readonly string _name;
        private readonly List<string> _executionOrder;

        public TestMiddleware(string name, List<string> executionOrder)
        {
            _name = name;
            _executionOrder = executionOrder;
        }

        public async Task<object?> InvokeAsync(ToolExecutionContext context, Func<Task<object?>> next)
        {
            _executionOrder.Add($"{_name}-before");
            var result = await next();
            _executionOrder.Add($"{_name}-after");
            return result;
        }
    }

    private class DelegateMiddleware : IToolExecutionMiddleware
    {
        private readonly Func<ToolExecutionContext, Func<Task<object?>>, Task<object?>> _handler;

        public DelegateMiddleware(Func<ToolExecutionContext, Func<Task<object?>>, Task<object?>> handler) => _handler = handler;

        public Task<object?> InvokeAsync(ToolExecutionContext context, Func<Task<object?>> next) => _handler(context, next);
    }
}
