
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// <see cref="McpClientAdapter"/> 的工具相关 partial — 工具列举与 MCP Tool → AIFunction 转换。
/// </summary>
internal sealed partial class McpClientAdapter
{
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
