namespace Tnzi.AI.Tools;

/// <summary>
/// 工具适配器 - 将 ToolDefinition 转换为 M.E.AI 的 AITool
/// </summary>
/// <remarks>
/// 工具提供者（ToolDefinition.ProviderType）必须注册为 Singleton，
/// 因为 ToolAdapter 从根 ServiceProvider 解析实例（非 Scoped）。
/// </remarks>
internal static class ToolAdapter
{
    /// <summary>
    /// 将 ToolDefinition 列表转换为 M.E.AI 的 AITool 列表
    /// </summary>
    internal static List<AITool> ConvertToAITools(
        IReadOnlyList<ToolDefinition> tools,
        IServiceProvider serviceProvider)
    {
        return tools
            .Select(tool => ConvertToAITool(tool, serviceProvider))
            .Where(t => t != null)
            .Cast<AITool>()
            .ToList();
    }

    /// <summary>
    /// 将单个 ToolDefinition 转换为 M.E.AI 的 AIFunction
    /// </summary>
    private static AIFunction? ConvertToAITool(ToolDefinition tool, IServiceProvider serviceProvider)
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
            var createInstanceFunc = new Func<AIFunctionArguments, object>(args =>
            {
                var instance = serviceProvider.GetService(tool.ProviderType);
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
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger(typeof(ToolAdapter));
            logger?.LogWarning(ex, "Failed to convert tool '{ToolName}' to AIFunction", tool.Name);
            return null;
        }
    }
}
