using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Tnzi.AspNetCore.Mvc.Conventions;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// 端点级抑制：按 <see cref="SensitiveEndpointAttribute.Name"/> 摘掉单个 action，
/// 而不是像 <c>DisabledControllers</c> 那样连整个控制器一起关掉。
/// </summary>
public class SensitiveEndpointFilterTests
{
    [Fact]
    public void NoDisabledEndpoints_KeepsEveryAction()
    {
        var context = CreateContext(typeof(SampleController));

        new ConfigurationControllerFilterProvider(new ControllerFilterOptions())
            .OnProvidersExecuting(context);

        Assert.Equal(3, context.Result.Controllers[0].Actions.Count);
    }

    [Fact]
    public void DisabledEndpoint_RemovesOnlyThatAction()
    {
        var context = CreateContext(typeof(SampleController));
        var options = new ControllerFilterOptions { DisabledEndpoints = ["test.portable"] };

        new ConfigurationControllerFilterProvider(options).OnProvidersExecuting(context);

        var remaining = ActionNames(context);
        Assert.DoesNotContain(nameof(SampleController.MintToken), remaining);
        // 同一个控制器的其余端点必须活着 —— 这正是端点级抑制存在的理由。
        Assert.Contains(nameof(SampleController.ShareLink), remaining);
        Assert.Contains(nameof(SampleController.Ordinary), remaining);
    }

    [Fact]
    public void DisabledEndpoint_SupportsWildcard()
    {
        var context = CreateContext(typeof(SampleController));
        var options = new ControllerFilterOptions { DisabledEndpoints = ["test.*"] };

        new ConfigurationControllerFilterProvider(options).OnProvidersExecuting(context);

        // 两个标记端点都被摘掉，未标记的那个不受影响。
        Assert.Equal([nameof(SampleController.Ordinary)], ActionNames(context));
    }

    [Fact]
    public void SharedCapabilityName_RemovesEveryActionThatCarriesIt()
    {
        var context = CreateContext(typeof(TwinController));
        var options = new ControllerFilterOptions { DisabledEndpoints = ["test.twin"] };

        new ConfigurationControllerFilterProvider(options).OnProvidersExecuting(context);

        // 单个与批量是同一条能力（存储的 access-token 正是这样），关一次要两个都关掉，
        // 否则批量端点会成为刚被关掉那条能力的旁路。
        Assert.Empty(context.Result.Controllers[0].Actions);
    }

    [Fact]
    public void UnmarkedAction_CannotBeSuppressed_EvenWhenNameLooksLikeAMatch()
    {
        var context = CreateContext(typeof(SampleController));
        // 按方法名而不是能力名去配：不该有任何效果。
        var options = new ControllerFilterOptions { DisabledEndpoints = ["Ordinary", "*"] };

        new ConfigurationControllerFilterProvider(options).OnProvidersExecuting(context);

        // `*` 摘掉了两个标记端点，但没标记的 Ordinary 必须留下 ——
        // 能按任意方法名关端点的配置项会变成绕开代码审查改 API 表面的工具。
        Assert.Contains(nameof(SampleController.Ordinary), ActionNames(context));
    }

    [Fact]
    public void DisabledEndpoint_MatchingNothing_LogsWarning()
    {
        var context = CreateContext(typeof(SampleController));
        var logs = new List<string>();
        var options = new ControllerFilterOptions { DisabledEndpoints = ["test.portabel"] }; // 拼错

        new ConfigurationControllerFilterProvider(options, new CapturingLoggerFactory(logs))
            .OnProvidersExecuting(context);

        // 名字写错与"已经关掉了"在运行时长得一模一样，这条告警是唯一的区别。
        Assert.Contains(logs, m => m.Contains("test.portabel", StringComparison.Ordinal));
        Assert.Equal(3, context.Result.Controllers[0].Actions.Count);
    }

    [Fact]
    public void DisabledEndpoint_ThatMatched_LogsNothing()
    {
        var context = CreateContext(typeof(SampleController));
        var logs = new List<string>();
        var options = new ControllerFilterOptions { DisabledEndpoints = ["test.portable"] };

        new ConfigurationControllerFilterProvider(options, new CapturingLoggerFactory(logs))
            .OnProvidersExecuting(context);

        Assert.Empty(logs);
    }

    private static List<string> ActionNames(ApplicationModelProviderContext context)
        => [.. context.Result.Controllers[0].Actions.Select(a => a.ActionMethod.Name)];

    private static ApplicationModelProviderContext CreateContext(params Type[] controllerTypes)
    {
        var context = new ApplicationModelProviderContext(
            [.. controllerTypes.Select(t => t.GetTypeInfo())]);

        context.Result.Controllers.Clear();

        foreach (var type in controllerTypes)
        {
            var controller = new ControllerModel(type.GetTypeInfo(), [])
            {
                ControllerName = type.Name
            };

            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var method in methods)
            {
                controller.Actions.Add(
                    new ActionModel(method, [.. method.GetCustomAttributes(inherit: true)])
                    {
                        ActionName = method.Name
                    });
            }

            context.Result.Controllers.Add(controller);
        }

        return context;
    }

    private class SampleController : Controller
    {
        [SensitiveEndpoint("test.portable", "Issues a credential that can leave the controlled environment.")]
        public void MintToken()
        {
        }

        [SensitiveEndpoint("test.share", "Serves content to unauthenticated callers.")]
        public void ShareLink()
        {
        }

        public void Ordinary()
        {
        }
    }

    private class TwinController : Controller
    {
        [SensitiveEndpoint("test.twin", "Single form.")]
        public void One()
        {
        }

        [SensitiveEndpoint("test.twin", "Batch form of the same capability.")]
        public void Many()
        {
        }
    }

    private sealed class CapturingLoggerFactory(List<string> sink) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => sink.Add(formatter(state, exception));
        }
    }
}
