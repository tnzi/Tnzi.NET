
namespace Tnzi.AI.Infrastructure.ContextProviders.Contributors;

/// <summary>
/// Deferred Tool 上下文贡献者 — 注入 deferred 工具引导信息
/// </summary>
internal sealed class DeferredToolContributor : IContextProviderContributor
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Deferred tools 始终在最后注入（SkillContextProvider.DeferredTools 之后）
    /// </summary>
    public int Order => ContextProviderOrders.Skills + 10;

    public DeferredToolContributor(ILoggerFactory loggerFactory)
    {
        _loggerFactory = Check.NotNull(loggerFactory);
    }

    /// <summary>
    /// 始终创建（无需配置开关，仅在 ToolDeferredRegistry 有条目时才真正注入内容）
    /// </summary>
    public IContextProvider? TryCreate(ContextProviderCreationContext context)
    {
        var logger = _loggerFactory.CreateLogger<DeferredToolContextProvider>();
        return new DeferredToolContextProvider(logger);
    }
}
