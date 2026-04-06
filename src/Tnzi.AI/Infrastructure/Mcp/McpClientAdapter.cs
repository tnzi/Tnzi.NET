
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// 包装 SDK McpClient，暴露 ListToolsAsync 返回 AITool 列表并实现 IAsyncDisposable。
/// 仅将类型为 AITool 的项返回；若 SDK 返回协议层 Tool 等非 AITool 类型，会被跳过（需在适配层增加 MCP Tool → AITool 转换）。
/// </summary>
internal sealed class McpClientAdapter : IMcpClientAdapter
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken ct = default)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var list = new List<AITool>();
        var skipped = 0;
        var converted = 0;
        foreach (var tool in tools)
        {
            if (tool is AITool aiTool)
            {
                list.Add(aiTool);
            }
            else
            {
                var convertedTool = TryConvertToAIFunction(tool);
                if (convertedTool != null)
                {
                    converted++;
                    list.Add(convertedTool);
                }
                else
                {
                    skipped++;
                    _logger.LogDebug("MCP server '{ServerName}' tool '{Name}' is not AITool, skipping", _serverName, tool?.Name);
                }
            }
        }
        if (skipped > 0 && list.Count == 0)
        {
            _logger.LogWarning(
                "MCP server '{ServerName}' returned {Count} tools but none were AITool. If SDK returns protocol Tool type, add MCP Tool to AITool conversion.",
                _serverName, tools.Count);
        }
        if (converted > 0)
        {
            _logger.LogDebug(
                "MCP server '{ServerName}' converted {ConvertedCount} tools to AIFunction",
                _serverName, converted);
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpResourceInfo>> ListResourcesAsync(CancellationToken ct = default)
    {
        try
        {
            if (_listResourcesMethod == null)
            {
                _logger.LogDebug("MCP server '{ServerName}' client does not support ListResourcesAsync", _serverName);
                return Array.Empty<McpResourceInfo>();
            }

            var taskObj = InvokeWithCancellation(_listResourcesMethod, ct);
            if (taskObj == null)
            {
                return Array.Empty<McpResourceInfo>();
            }

            var result = await AwaitAndExtractResultAsync(taskObj).ConfigureAwait(false);
            return ConvertToResourceInfoList(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list resources from MCP server '{ServerName}'", _serverName);
            return Array.Empty<McpResourceInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<McpResourceContent?> ReadResourceAsync(string uri, CancellationToken ct = default)
    {
        try
        {
            if (_readResourceMethod == null)
            {
                _logger.LogDebug("MCP server '{ServerName}' client does not support ReadResourceAsync", _serverName);
                return null;
            }

            var parameters = _readResourceMethod.GetParameters();
            object? taskObj;

            // ReadResourceAsync(string uri, CancellationToken ct) 或 ReadResourceAsync(Uri uri, CancellationToken ct)
            if (parameters.Length == 2 && parameters[1].ParameterType == typeof(CancellationToken))
            {
                var uriArg = parameters[0].ParameterType == typeof(Uri) ? (object)new Uri(uri) : uri;
                taskObj = _readResourceMethod.Invoke(_client, [uriArg, ct]);
            }
            else if (parameters.Length == 1)
            {
                var uriArg = parameters[0].ParameterType == typeof(Uri) ? (object)new Uri(uri) : uri;
                taskObj = _readResourceMethod.Invoke(_client, [uriArg]);
            }
            else
            {
                // 尝试带 CancellationToken 的最后一个参数
                var args = new object?[parameters.Length];
                var uriArg = parameters[0].ParameterType == typeof(Uri) ? (object)new Uri(uri) : uri;
                args[0] = uriArg;
                for (var i = 1; i < parameters.Length; i++)
                {
                    args[i] = parameters[i].ParameterType == typeof(CancellationToken) ? ct : GetDefault(parameters[i].ParameterType);
                }
                taskObj = _readResourceMethod.Invoke(_client, args);
            }

            if (taskObj == null)
            {
                return null;
            }

            var result = await AwaitAndExtractResultAsync(taskObj).ConfigureAwait(false);
            return ConvertToResourceContent(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read resource '{Uri}' from MCP server '{ServerName}'", uri, _serverName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<McpPromptInfo>> ListPromptsAsync(CancellationToken ct = default)
    {
        try
        {
            if (_listPromptsMethod == null)
            {
                _logger.LogDebug("MCP server '{ServerName}' client does not support ListPromptsAsync", _serverName);
                return Array.Empty<McpPromptInfo>();
            }

            var taskObj = InvokeWithCancellation(_listPromptsMethod, ct);
            if (taskObj == null)
            {
                return Array.Empty<McpPromptInfo>();
            }

            var result = await AwaitAndExtractResultAsync(taskObj).ConfigureAwait(false);
            return ConvertToPromptInfoList(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list prompts from MCP server '{ServerName}'", _serverName);
            return Array.Empty<McpPromptInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<McpPromptResult?> GetPromptAsync(string promptName, Dictionary<string, string>? arguments = null, CancellationToken ct = default)
    {
        try
        {
            if (_getPromptMethod == null)
            {
                _logger.LogDebug("MCP server '{ServerName}' client does not support GetPromptAsync", _serverName);
                return null;
            }

            var parameters = _getPromptMethod.GetParameters();
            var args = new object?[parameters.Length];

            // 第一个参数: promptName (string)
            args[0] = promptName;

            for (var i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(CancellationToken))
                {
                    args[i] = ct;
                }
                else if (IsDictionaryLikeStringString(parameters[i].ParameterType))
                {
                    args[i] = arguments;
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

            var taskObj = _getPromptMethod.Invoke(_client, args);
            if (taskObj == null)
            {
                return null;
            }

            var result = await AwaitAndExtractResultAsync(taskObj).ConfigureAwait(false);
            return ConvertToPromptResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get prompt '{PromptName}' from MCP server '{ServerName}'", promptName, _serverName);
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private AIFunction? TryConvertToAIFunction(object tool)
    {
        if (tool == null)
        {
            return null;
        }

        try
        {
            var type = tool.GetType();
            var name = type.GetProperty("Name")?.GetValue(tool) as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var description = type.GetProperty("Description")?.GetValue(tool) as string ?? string.Empty;
            var displayName = GetDisplayName(name);
            var invoker = new McpToolInvoker(_client, name, _serverName, _logger);
            var inputSchema = TryGetInputSchema(tool);

            var options = new AIFunctionFactoryOptions
            {
                Name = displayName,
                Description = description,
                AdditionalProperties = BuildAdditionalProperties(name, inputSchema)
            };

            AIFunction function = AIFunctionFactory.Create(invoker.InvokeAsync, options);
            if (inputSchema.HasValue)
            {
                function = new McpSchemaAIFunction(function, inputSchema.Value);
            }

            return function;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert MCP tool to AIFunction for server '{ServerName}'", _serverName);
            return null;
        }
    }

    private string GetDisplayName(string toolName)
    {
        if (!_prefixToolNameWithServer || string.IsNullOrWhiteSpace(_serverName))
        {
            return toolName;
        }

        return $"mcp:{_serverName}/{toolName}";
    }

    private Dictionary<string, object?> BuildAdditionalProperties(string toolName, JsonElement? inputSchema)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mcp.server"] = _serverName,
            ["mcp.originalName"] = toolName
        };
        if (inputSchema.HasValue)
        {
            dict["mcp.inputSchema"] = inputSchema.Value;
        }
        return dict;
    }

    private JsonElement? TryGetInputSchema(object tool)
    {
        try
        {
            var type = tool.GetType();
            object? schemaObj =
                type.GetProperty("InputSchema")?.GetValue(tool) ??
                type.GetProperty("Parameters")?.GetValue(tool) ??
                type.GetProperty("Schema")?.GetValue(tool);

            if (schemaObj == null)
            {
                return null;
            }

            if (schemaObj is JsonElement jsonElement)
            {
                return jsonElement;
            }

            if (schemaObj is JsonDocument jsonDocument)
            {
                return jsonDocument.RootElement.Clone();
            }

            if (schemaObj is string jsonText && !string.IsNullOrWhiteSpace(jsonText))
            {
                using var doc = JsonDocument.Parse(jsonText);
                return doc.RootElement.Clone();
            }

            return JsonSerializer.SerializeToElement(schemaObj);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse MCP tool input schema for server '{ServerName}'", _serverName);
            return null;
        }
    }

    /// <summary>
    /// 通过反射调用 ModelContextProtocol SDK 的 CallToolAsync(string, payload, CancellationToken) 执行 MCP 工具。
    /// 依赖 SDK 的该方法签名；SDK 升级时如遇调用失败需核对该方法签名与参数类型。
    /// </summary>
    private sealed class McpToolInvoker
    {
        private readonly McpClient _client;
        private readonly string _toolName;
        private readonly string _serverName;
        private readonly ILogger _logger;
        private readonly MethodInfo _callToolMethod;
        private readonly CallToolArgKind _callToolArgKind;

        public McpToolInvoker(McpClient client, string toolName, string serverName, ILogger logger)
        {
            _client = Check.NotNull(client);
            _toolName = toolName;
            _serverName = serverName;
            _logger = Check.NotNull(logger);
            (_callToolMethod, _callToolArgKind) = ResolveCallToolMethod(client.GetType());
        }

        public async Task<object?> InvokeAsync(AIFunctionArguments arguments, CancellationToken ct = default)
        {
            var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (arguments != null)
            {
                foreach (var kv in arguments)
                {
                    args[kv.Key] = kv.Value;
                }
            }

            try
            {
                return await CallToolAsync(args, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP tool call failed: Server='{ServerName}', Tool='{ToolName}'", _serverName, _toolName);
                throw;
            }
        }

        private async Task<object?> CallToolAsync(Dictionary<string, object?> args, CancellationToken ct)
        {
            if (_callToolMethod == null)
            {
                throw new InvalidOperationException("MCP client does not expose a supported CallToolAsync overload.");
            }

            object? argPayload = _callToolArgKind switch
            {
                CallToolArgKind.Dictionary => args,
                CallToolArgKind.JsonElement => JsonSerializer.SerializeToElement(args),
                CallToolArgKind.JsonString => JsonSerializer.Serialize(args),
                _ => args
            };

            var taskObj = _callToolMethod.Invoke(_client, new[] { _toolName, argPayload!, ct });
            if (taskObj == null)
            {
                return null;
            }

            return await AwaitTaskResultAsync(taskObj).ConfigureAwait(false);
        }

        /// <summary>
        /// 解析 McpClient 上的 CallToolAsync(string, payload, CancellationToken) 方法；依赖 ModelContextProtocol SDK 的该签名，SDK 升级时需复核。
        /// </summary>
        private static (MethodInfo Method, CallToolArgKind Kind) ResolveCallToolMethod(Type clientType)
        {
            var methods = clientType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => string.Equals(m.Name, "CallToolAsync", StringComparison.Ordinal))
                .ToList();

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 3)
                {
                    continue;
                }

                if (parameters[0].ParameterType != typeof(string) ||
                    parameters[2].ParameterType != typeof(CancellationToken))
                {
                    continue;
                }

                var payloadType = parameters[1].ParameterType;
                if (IsDictionaryLike(payloadType))
                {
                    return (method, CallToolArgKind.Dictionary);
                }
                if (payloadType == typeof(JsonElement))
                {
                    return (method, CallToolArgKind.JsonElement);
                }
                if (payloadType == typeof(string))
                {
                    return (method, CallToolArgKind.JsonString);
                }
            }

            throw new InvalidOperationException("MCP client does not expose a supported CallToolAsync overload.");
        }

        private static bool IsDictionaryLike(Type payloadType)
        {
            if (typeof(IDictionary<string, object?>).IsAssignableFrom(payloadType))
            {
                return true;
            }
            if (typeof(IDictionary<string, object>).IsAssignableFrom(payloadType))
            {
                return true;
            }
            if (typeof(IReadOnlyDictionary<string, object?>).IsAssignableFrom(payloadType))
            {
                return true;
            }
            if (typeof(IReadOnlyDictionary<string, object>).IsAssignableFrom(payloadType))
            {
                return true;
            }

            return false;
        }

        private static async Task<object?> AwaitTaskResultAsync(object taskObj)
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

        private enum CallToolArgKind
        {
            Dictionary,
            JsonElement,
            JsonString
        }
    }

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
    /// 将 SDK 返回的资源列表转为 McpResourceInfo 列表。
    /// </summary>
    private IReadOnlyList<McpResourceInfo> ConvertToResourceInfoList(object? result)
    {
        if (result == null)
        {
            return Array.Empty<McpResourceInfo>();
        }

        var list = new List<McpResourceInfo>();
        if (result is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                var info = ExtractResourceInfo(item);
                if (info != null)
                {
                    list.Add(info);
                }
            }
        }
        return list;
    }

    /// <summary>
    /// 从 SDK Resource 对象中提取 McpResourceInfo。
    /// </summary>
    private McpResourceInfo? ExtractResourceInfo(object? item)
    {
        if (item == null)
        {
            return null;
        }

        try
        {
            var type = item.GetType();
            var uri = (type.GetProperty("Uri")?.GetValue(item))?.ToString() ?? string.Empty;
            var name = type.GetProperty("Name")?.GetValue(item) as string ?? string.Empty;
            var mimeType = type.GetProperty("MimeType")?.GetValue(item) as string;
            var description = type.GetProperty("Description")?.GetValue(item) as string;

            if (string.IsNullOrWhiteSpace(uri))
            {
                return null;
            }

            return new McpResourceInfo(uri, name, mimeType, description);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract resource info from MCP server '{ServerName}'", _serverName);
            return null;
        }
    }

    /// <summary>
    /// 将 SDK 返回的 ReadResource 结果转为 McpResourceContent。
    /// </summary>
    private McpResourceContent? ConvertToResourceContent(object? result)
    {
        if (result == null)
        {
            return null;
        }

        try
        {
            var type = result.GetType();

            // SDK ReadResourceResult 通常有 Contents 属性（ResourceContents 列表）
            var contentsProp = type.GetProperty("Contents");
            if (contentsProp != null)
            {
                var contents = contentsProp.GetValue(result);
                if (contents is System.Collections.IEnumerable enumerable)
                {
                    foreach (var content in enumerable)
                    {
                        return ExtractResourceContent(content);
                    }
                }
            }

            // 降级：直接从 result 提取 Text/MimeType
            return ExtractResourceContent(result);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to convert resource content from MCP server '{ServerName}'", _serverName);
            return null;
        }
    }

    /// <summary>
    /// 从单个 ResourceContents 对象提取 McpResourceContent。
    /// </summary>
    private McpResourceContent? ExtractResourceContent(object? item)
    {
        if (item == null)
        {
            return null;
        }

        try
        {
            var type = item.GetType();
            var text = type.GetProperty("Text")?.GetValue(item) as string;
            var mimeType = type.GetProperty("MimeType")?.GetValue(item) as string;

            // 某些 SDK 版本使用 Blob 属性
            if (text == null)
            {
                var blob = type.GetProperty("Blob")?.GetValue(item);
                if (blob is byte[] bytes)
                {
                    text = Convert.ToBase64String(bytes);
                }
                else if (blob is string blobStr)
                {
                    text = blobStr;
                }
            }

            if (text == null)
            {
                return null;
            }

            return new McpResourceContent(text, mimeType);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract resource content from MCP server '{ServerName}'", _serverName);
            return null;
        }
    }

    /// <summary>
    /// 将 SDK 返回的 Prompt 列表转为 McpPromptInfo 列表。
    /// </summary>
    private IReadOnlyList<McpPromptInfo> ConvertToPromptInfoList(object? result)
    {
        if (result == null)
        {
            return Array.Empty<McpPromptInfo>();
        }

        var list = new List<McpPromptInfo>();
        if (result is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                var info = ExtractPromptInfo(item);
                if (info != null)
                {
                    list.Add(info);
                }
            }
        }
        return list;
    }

    /// <summary>
    /// 从 SDK Prompt 对象中提取 McpPromptInfo。
    /// </summary>
    private McpPromptInfo? ExtractPromptInfo(object? item)
    {
        if (item == null)
        {
            return null;
        }

        try
        {
            var type = item.GetType();
            var name = type.GetProperty("Name")?.GetValue(item) as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var description = type.GetProperty("Description")?.GetValue(item) as string;
            var argumentsProp = type.GetProperty("Arguments");
            IReadOnlyList<McpPromptArgument>? arguments = null;

            if (argumentsProp != null)
            {
                var argsObj = argumentsProp.GetValue(item);
                if (argsObj is System.Collections.IEnumerable argsEnumerable)
                {
                    var argList = new List<McpPromptArgument>();
                    foreach (var arg in argsEnumerable)
                    {
                        var argInfo = ExtractPromptArgument(arg);
                        if (argInfo != null)
                        {
                            argList.Add(argInfo);
                        }
                    }
                    if (argList.Count > 0)
                    {
                        arguments = argList;
                    }
                }
            }

            return new McpPromptInfo(name, description, arguments);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract prompt info from MCP server '{ServerName}'", _serverName);
            return null;
        }
    }

    /// <summary>
    /// 从 SDK PromptArgument 对象中提取 McpPromptArgument。
    /// </summary>
    private McpPromptArgument? ExtractPromptArgument(object? item)
    {
        if (item == null)
        {
            return null;
        }

        try
        {
            var type = item.GetType();
            var name = type.GetProperty("Name")?.GetValue(item) as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var description = type.GetProperty("Description")?.GetValue(item) as string;
            var required = type.GetProperty("Required")?.GetValue(item) is true;

            return new McpPromptArgument(name, description, required);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract prompt argument from MCP server '{ServerName}'", _serverName);
            return null;
        }
    }

    /// <summary>
    /// 将 SDK 返回的 GetPrompt 结果转为 McpPromptResult。
    /// </summary>
    private McpPromptResult? ConvertToPromptResult(object? result)
    {
        if (result == null)
        {
            return null;
        }

        try
        {
            var type = result.GetType();

            // SDK GetPromptResult 通常有 Messages 属性
            var messagesProp = type.GetProperty("Messages");
            if (messagesProp != null)
            {
                var messagesObj = messagesProp.GetValue(result);
                if (messagesObj is System.Collections.IEnumerable enumerable)
                {
                    var messages = new List<McpPromptMessage>();
                    foreach (var msg in enumerable)
                    {
                        var message = ExtractPromptMessage(msg);
                        if (message != null)
                        {
                            messages.Add(message);
                        }
                    }
                    if (messages.Count > 0)
                    {
                        return new McpPromptResult(messages);
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to convert prompt result from MCP server '{ServerName}'", _serverName);
            return null;
        }
    }

    /// <summary>
    /// 从 SDK PromptMessage 对象中提取 McpPromptMessage。
    /// </summary>
    private McpPromptMessage? ExtractPromptMessage(object? item)
    {
        if (item == null)
        {
            return null;
        }

        try
        {
            var type = item.GetType();

            // Role 可能是 string 或 enum
            var roleObj = type.GetProperty("Role")?.GetValue(item);
            var role = roleObj?.ToString()?.ToLowerInvariant() ?? "user";

            // Content 可能是 string 或复杂对象
            var contentObj = type.GetProperty("Content")?.GetValue(item);
            string? content = null;

            if (contentObj is string contentStr)
            {
                content = contentStr;
            }
            else if (contentObj != null)
            {
                // 尝试从 Content 对象中提取 Text 属性
                var text = contentObj.GetType().GetProperty("Text")?.GetValue(contentObj) as string;
                content = text ?? contentObj.ToString();
            }

            if (content == null)
            {
                return null;
            }

            return new McpPromptMessage(role, content);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract prompt message from MCP server '{ServerName}'", _serverName);
            return null;
        }
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

    private sealed class McpSchemaAIFunction : DelegatingAIFunction
    {
        private readonly JsonElement _jsonSchema;

        public McpSchemaAIFunction(AIFunction innerFunction, JsonElement jsonSchema)
            : base(innerFunction)
        {
            _jsonSchema = jsonSchema;
        }

        public override JsonElement JsonSchema => _jsonSchema;
    }
}