
namespace Tnzi.AI.Infrastructure.Mcp;

/// <summary>
/// <see cref="McpClientAdapter"/> 的资源/提示相关 partial — Resource/Prompt 的列举与读取（经 SDK 反射）。
/// </summary>
internal sealed partial class McpClientAdapter
{
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
}
