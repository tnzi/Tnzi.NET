namespace Tnzi.AI.Tools;

/// <summary>
/// 工具适配器 - 将 ToolDefinition 转换为 M.E.AI 的 AITool
/// </summary>
/// <remarks>
/// 工具提供者（ToolDefinition.ProviderType）可注册为 Singleton 或 Scoped。
/// 工具实例在调用时通过 <see cref="ToolContextAccessor.Current"/> 的 RequestServices 解析（request-scoped），
/// 不可用时才回退到根 ServiceProvider。这允许工具提供者通过构造函数注入 Scoped 服务（如 IRepository、领域服务等）。
/// </remarks>
internal static class ToolAdapter
{
    /// <summary>
    /// 将 ToolDefinition 列表转换为 M.E.AI 的 AITool 列表
    /// </summary>
    internal static List<AITool> ConvertToAITools(
        IReadOnlyList<ToolDefinition> tools,
        IServiceProvider serviceProvider,
        ILogger? logger = null)
    {
        return tools
            .Select(tool => ConvertToAITool(tool, serviceProvider, logger))
            .Where(t => t != null)
            .Cast<AITool>()
            .ToList();
    }

    /// <summary>
    /// 将单个 ToolDefinition 转换为 M.E.AI 的 AIFunction
    /// </summary>
    private static AIFunction? ConvertToAITool(ToolDefinition tool, IServiceProvider serviceProvider, ILogger? logger)
    {
        Check.NotNull(tool);

        if (tool.MethodInfo == null)
        {
            return null;
        }

        try
        {
            // 使用 AIFunctionFactory 从 MethodInfo 创建 AIFunction
            // createInstanceFunc 从 DI 容器获取工具提供者实例
            // 优先使用 ToolContextAccessor.Current?.RequestServices（执行时为 request-scoped）
            // 以支持注册为 Scoped 的工具提供者（需要访问 Scoped 服务的工具）
            var createInstanceFunc = new Func<AIFunctionArguments, object>(args =>
            {
                var sp = ToolContextAccessor.Current?.RequestServices ?? serviceProvider;
                var instance = sp.GetService(tool.ProviderType);
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        $"Tool provider '{tool.ProviderType.Name}' not registered in DI container. " +
                        $"Please register it with services.AddScoped<{tool.ProviderType.Name}>()");
                }
                return instance;
            });

            var aiFunction = AIFunctionFactory.Create(
                method: tool.MethodInfo,
                createInstanceFunc: createInstanceFunc,
                options: new AIFunctionFactoryOptions
                {
                    Name = tool.Name,
                    Description = tool.Description ?? string.Empty
                });

            return aiFunction;
        }
        catch (Exception ex)
        {
            // 记录错误但不抛出，允许其他工具继续工作
            logger?.LogWarning(ex, "Failed to convert tool '{ToolName}' to AIFunction", tool.Name);
            return null;
        }
    }
}
