
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 包装 SDK McpClient，暴露 ListToolsAsync 返回 AITool 列表并实现 IAsyncDisposable。
/// 仅将类型为 AITool 的项返回；若 SDK 返回协议层 Tool 等非 AITool 类型，会被跳过（需在适配层增加 MCP Tool → AITool 转换）。
/// </summary>
/// <remarks>
/// 按职责拆分为多个 partial 文件：
/// <list type="bullet">
///   <item><c>McpClientAdapter.cs</c> — 字段/构造/共享反射辅助方法</item>
///   <item><c>McpClientAdapter.Tools.cs</c> — 工具列举与 MCP Tool → AIFunction 转换</item>
///   <item><c>McpClientAdapter.Resources.cs</c> — 资源/提示（Resource/Prompt）列举与读取</item>
///   <item><c>McpClientAdapter.Lifecycle.cs</c> — 释放（DisposeAsync）</item>
/// </list>
/// </remarks>
internal sealed partial class McpClientAdapter : IMcpClientAdapter
{
    private readonly McpClient _client;
    private readonly string _serverName;
    private readonly ILogger _logger;
    private readonly bool _prefixToolNameWithServer;

    // 反射缓存 — 构造时一次性解析，避免每次调用 GetMethod()
    private readonly MethodInfo? _listResourcesMethod;
    private readonly MethodInfo? _readResourceMethod;
    private readonly MethodInfo? _listPromptsMethod;
    private readonly MethodInfo? _getPromptMethod;

    public McpClientAdapter(McpClient client, string serverName, ILogger logger, bool prefixToolNameWithServer)
    {
        _client = Check.NotNull(client);
        _serverName = serverName ?? string.Empty;
        _logger = Check.NotNull(logger);
        _prefixToolNameWithServer = prefixToolNameWithServer;

        // 一次性缓存反射方法引用（使用 GetMethods + FirstOrDefault 避免多重载导致 AmbiguousMatchException）
        var clientType = _client.GetType();
        var allMethods = clientType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
        _listResourcesMethod = allMethods
            .FirstOrDefault(m => string.Equals(m.Name, "ListResourcesAsync", StringComparison.Ordinal));
        _readResourceMethod = allMethods
            .FirstOrDefault(m => string.Equals(m.Name, "ReadResourceAsync", StringComparison.Ordinal)
                                 && m.GetParameters().Length >= 1);
        _listPromptsMethod = allMethods
            .FirstOrDefault(m => string.Equals(m.Name, "ListPromptsAsync", StringComparison.Ordinal));
        _getPromptMethod = allMethods
            .FirstOrDefault(m => string.Equals(m.Name, "GetPromptAsync", StringComparison.Ordinal)
                                 && m.GetParameters().Length >= 1);
    }

    // =====================================================================
    // 共享反射辅助方法（被 Tools / Resources partial 共用）
    // =====================================================================

    /// <summary>
    /// 调用方法并传入 CancellationToken 参数。
    /// </summary>
    private object? InvokeWithCancellation(MethodInfo method, CancellationToken ct)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].ParameterType == typeof(CancellationToken))
            {
                args[i] = ct;
            }
            else if (parameters[i].HasDefaultValue)
            {
                args[i] = parameters[i].DefaultValue;
            }
            else
            {
                args[i] = GetDefault(parameters[i].ParameterType);
            }
        }
        return method.Invoke(_client, args);
    }

    /// <summary>
    /// 等待 Task 并提取 Result 值。
    /// </summary>
    private static async Task<object?> AwaitAndExtractResultAsync(object taskObj)
    {
        if (taskObj is Task task)
        {
            await task.ConfigureAwait(false);
            var taskType = task.GetType();
            if (taskType.IsGenericType)
            {
                return taskType.GetProperty("Result")?.GetValue(task);
            }
            return null;
        }
        return taskObj;
    }

    /// <summary>
    /// 检查类型是否为 Dictionary&lt;string, string&gt; 兼容类型。
    /// </summary>
    private static bool IsDictionaryLikeStringString(Type type)
    {
        return typeof(IDictionary<string, string>).IsAssignableFrom(type)
               || typeof(IReadOnlyDictionary<string, string>).IsAssignableFrom(type)
               || typeof(Dictionary<string, string>).IsAssignableFrom(type);
    }

    private static object? GetDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
}
