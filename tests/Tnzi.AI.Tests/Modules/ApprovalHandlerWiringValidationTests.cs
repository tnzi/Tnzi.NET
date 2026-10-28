namespace Tnzi.AI.Tests.Modules;

/// <summary>
/// 启动期提示：配置了需要审批的工具，而回答审批的是默认的自动批准处理器（或根本没有处理器）。
/// </summary>
/// <remarks>
/// 框架默认注册 <see cref="AutoApprovalHandler"/>，它对每一个 Ask 决策直接批准。管理员把某个工具设成
/// AlwaysRequire、写一条 Ask 规则，期待的是「有人确认」，实际一个人都没问，调用照常执行，只留一条外观正常的
/// <c>ApprovalRequestedEvent</c>。默认值本身不改（MCP 服务器的 ApprovalMode 默认就是 AlwaysRequire，
/// 改成失败关闭会让每个 MCP 工具开箱即被拒绝），但这件事必须在启动日志里说出来。
/// </remarks>
public class ApprovalHandlerWiringValidationTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class RealApprovalHandler : IToolApprovalHandler
    {
        public Task<ToolApprovalResult> RequestApprovalAsync(ToolApprovalRequest request, CancellationToken ct = default)
            => Task.FromResult(ToolApprovalResult.Reject("no"));
    }

    private static CapturingLogger Validate(Action<AIOptions> configure, IToolApprovalHandler? handler)
    {
        var options = new AIOptions();
        configure(options);

        var services = new ServiceCollection();
        services.AddSingleton<IOptions<AIOptions>>(new OptionsWrapper<AIOptions>(options));
        if (handler != null)
            services.AddSingleton(handler);
        using var provider = services.BuildServiceProvider();

        var logger = new CapturingLogger();
        AIModule.ValidateApprovalHandlerWiring(provider, logger);
        return logger;
    }

    private static readonly IToolApprovalHandler AutoApprove = new AutoApprovalHandler(NullLogger<AutoApprovalHandler>.Instance);

    [Fact]
    public void ToolApprovalRequired_WithTheAutoApprovingDefault_Warns()
    {
        var logger = Validate(o => o.ToolApproval = new ToolApprovalOptions { Enabled = true, Mode = ToolApprovalMode.AlwaysRequire }, AutoApprove);

        var warning = logger.Warnings.ShouldHaveSingleItem();
        warning.ShouldContain("AI:ToolApproval");
        warning.ShouldContain(nameof(AutoApprovalHandler));
    }

    [Fact]
    public void ConfiguredAskRule_WithTheAutoApprovingDefault_Warns()
    {
        var logger = Validate(o =>
        {
            o.Permissions.Enabled = true;
            o.Permissions.SystemRules.Add(new ToolPermissionRuleOptions { ToolPattern = "execute_command", Behavior = PermissionBehavior.Ask });
        }, AutoApprove);

        logger.Warnings.ShouldHaveSingleItem().ShouldContain("AI:Permissions");
    }

    [Fact]
    public void McpServerRequiringApproval_WithTheAutoApprovingDefault_Warns()
    {
        var logger = Validate(o =>
        {
            o.Mcp.Enabled = true;
            o.Mcp.Servers.Add(new McpServerConfig { Name = "files", ApprovalMode = McpApprovalMode.AlwaysRequire });
            o.Mcp.Servers.Add(new McpServerConfig { Name = "docs", ApprovalMode = McpApprovalMode.NeverRequire });
        }, AutoApprove);

        var warning = logger.Warnings.ShouldHaveSingleItem();
        warning.ShouldContain("files");
        warning.ShouldNotContain("docs");
    }

    [Fact]
    public void ApprovalRequired_WithNoHandlerAtAll_WarnsThatCallsAreRejected()
    {
        var logger = Validate(o => o.ToolApproval = new ToolApprovalOptions { Enabled = true, Mode = ToolApprovalMode.AlwaysRequire }, handler: null);

        logger.Warnings.ShouldHaveSingleItem().ShouldContain("rejected");
    }

    [Fact]
    public void ApprovalRequired_WithARealHandler_IsQuiet()
    {
        var logger = Validate(o => o.ToolApproval = new ToolApprovalOptions { Enabled = true, Mode = ToolApprovalMode.AlwaysRequire }, new RealApprovalHandler());

        logger.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void NothingRequiresApproval_IsQuiet()
    {
        var logger = Validate(o =>
        {
            o.Mcp.Enabled = true;
            o.Mcp.Servers.Add(new McpServerConfig { Name = "docs", ApprovalMode = McpApprovalMode.NeverRequire });
        }, AutoApprove);

        logger.Warnings.ShouldBeEmpty();
    }
}
