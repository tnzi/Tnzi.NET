namespace Tnzi.AI.Infrastructure.Tools;

/// <summary>
/// 从 OpenAPI 3.x 规范自动生成 AITool 列表
/// </summary>
/// <remarks>
/// <para>
/// 解析 OpenAPI JSON 规范，将每个 API 端点转换为一个 AITool，
/// 使 AI Agent 能够通过函数调用方式访问外部 REST API。
/// </para>
/// <para>
/// 支持 path 参数、query 参数和 request body 的提取，
/// 以及通过 IncludeOperations / ExcludeOperations 过滤端点。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "OpenAPI tool generation is evolving")]
public class OpenApiToolGenerator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AIOptions> _aiOptions;
    private readonly ILogger<OpenApiToolGenerator> _logger;

    public OpenApiToolGenerator(
        IHttpClientFactory httpClientFactory,
        IOptions<AIOptions> aiOptions,
        ILogger<OpenApiToolGenerator> logger)
    {
        _httpClientFactory = Check.NotNull(httpClientFactory);
        _aiOptions = Check.NotNull(aiOptions);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 从所有已配置的 OpenAPI 规范生成 AITool 列表
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>生成的 AITool 列表；禁用时返回空列表</returns>
    public async Task<IReadOnlyList<AITool>> GenerateToolsAsync(CancellationToken ct = default)
    {
        var options = _aiOptions.Value.OpenApiTools;
        if (options == null || !options.Enabled || options.Specs.Count == 0)
        {
            return Array.Empty<AITool>();
        }

        var allTools = new List<AITool>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in options.Specs)
        {
            if (string.IsNullOrWhiteSpace(spec.Url) || string.IsNullOrWhiteSpace(spec.Name))
            {
                _logger.LogWarning("Skipping OpenAPI spec with empty URL or Name");
                continue;
            }

            try
            {
                var tools = await GenerateToolsFromSpecAsync(spec, ct);
                foreach (var tool in tools)
                {
                    if (tool.Name != null && seenNames.Add(tool.Name))
                    {
                        allTools.Add(tool);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "OpenAPI tool '{ToolName}' from spec '{SpecName}' skipped (duplicate name)",
                            tool.Name, spec.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to generate tools from OpenAPI spec '{SpecName}' at '{SpecUrl}'. Skipping.",
                    spec.Name, spec.Url);
            }
        }

        _logger.LogInformation("Generated {ToolCount} OpenAPI tools from {SpecCount} specs",
            allTools.Count, options.Specs.Count);

        return allTools;
    }

    /// <summary>
    /// 从单个 OpenAPI 规范生成 AITool 列表
    /// </summary>
    /// <param name="spec">OpenAPI 规范配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>生成的 AITool 列表</returns>
    public async Task<IReadOnlyList<AITool>> GenerateToolsFromSpecAsync(OpenApiSpec spec, CancellationToken ct = default)
    {
        Check.NotNull(spec);

        var json = await FetchSpecAsync(spec, ct);
        var document = ParseOpenApiDocument(json);
        return GenerateToolsFromDocument(document, spec);
    }

    /// <summary>
    /// 从已解析的 OpenAPI 文档 JSON 生成 AITool 列表（可测试入口）
    /// </summary>
    /// <param name="openApiJson">OpenAPI JSON 字符串</param>
    /// <param name="spec">规范配置（用于过滤和请求头）</param>
    /// <returns>生成的 AITool 列表</returns>
    public IReadOnlyList<AITool> GenerateToolsFromJson(string openApiJson, OpenApiSpec spec)
    {
        Check.NotNullOrWhiteSpace(openApiJson);
        Check.NotNull(spec);

        var document = ParseOpenApiDocument(openApiJson);
        return GenerateToolsFromDocument(document, spec);
    }

    /// <summary>
    /// 拉取 OpenAPI 规范文档
    /// </summary>
    private async Task<string> FetchSpecAsync(OpenApiSpec spec, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("Tnzi.AI.OpenApi");

        // 设置自定义请求头
        foreach (var (key, value) in spec.Headers)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }

        var response = await client.GetAsync(spec.Url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// 解析 OpenAPI 文档 JSON
    /// </summary>
    private static JsonDocument ParseOpenApiDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Failed to parse OpenAPI document as JSON", ex);
        }
    }

    /// <summary>
    /// 从解析后的 OpenAPI 文档生成 AITool 列表
    /// </summary>
    private IReadOnlyList<AITool> GenerateToolsFromDocument(JsonDocument document, OpenApiSpec spec)
    {
        var tools = new List<AITool>();
        var root = document.RootElement;

        // 提取 servers 基础 URL
        var baseUrl = ExtractBaseUrl(root);

        // 解析 paths
        if (!root.TryGetProperty("paths", out var paths))
        {
            _logger.LogWarning("OpenAPI spec '{SpecName}' has no paths", spec.Name);
            return tools;
        }

        var includeSet = spec.IncludeOperations.Count > 0
            ? new HashSet<string>(spec.IncludeOperations, StringComparer.OrdinalIgnoreCase)
            : null;
        var excludeSet = spec.ExcludeOperations.Count > 0
            ? new HashSet<string>(spec.ExcludeOperations, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var pathItem in paths.EnumerateObject())
        {
            var path = pathItem.Name;

            foreach (var operation in pathItem.Value.EnumerateObject())
            {
                var method = operation.Name.ToUpperInvariant();

                // 仅处理标准 HTTP 方法
                if (!IsHttpMethod(method))
                {
                    continue;
                }

                var operationElement = operation.Value;

                // 获取 operationId
                var operationId = operationElement.TryGetProperty("operationId", out var opId)
                    ? opId.GetString()
                    : null;

                // 应用 Include/Exclude 过滤
                if (!ShouldIncludeOperation(operationId, includeSet, excludeSet))
                {
                    continue;
                }

                try
                {
                    var tool = CreateToolFromOperation(spec, baseUrl, path, method, operationElement, operationId);
                    if (tool != null)
                    {
                        tools.Add(tool);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Failed to create tool for {Method} {Path} in spec '{SpecName}'",
                        method, path, spec.Name);
                }
            }
        }

        return tools;
    }

    /// <summary>
    /// 从单个 OpenAPI 操作创建 AITool
    /// </summary>
    private AITool? CreateToolFromOperation(
        OpenApiSpec spec,
        string? baseUrl,
        string path,
        string method,
        JsonElement operation,
        string? operationId)
    {
        // 生成工具名称：优先使用 operationId，否则使用 method_path 格式
        var toolName = !string.IsNullOrWhiteSpace(operationId)
            ? SanitizeToolName(operationId)
            : GenerateToolName(method, path);

        // 如果配置了规范名称前缀，防止跨规范重名
        var fullToolName = $"{spec.Name}_{toolName}";

        // 构建描述
        var description = BuildDescription(operation, method, path);

        // 收集参数
        var parameters = new List<OpenApiParameter>();

        // 路径参数和查询参数
        if (operation.TryGetProperty("parameters", out var paramsArray))
        {
            foreach (var param in paramsArray.EnumerateArray())
            {
                var paramObj = ParseParameter(param);
                if (paramObj != null)
                {
                    parameters.Add(paramObj);
                }
            }
        }

        // 请求体
        var hasRequestBody = operation.TryGetProperty("requestBody", out var requestBody);
        if (hasRequestBody)
        {
            var bodyParams = ParseRequestBody(requestBody);
            parameters.AddRange(bodyParams);
        }

        // 创建 HTTP 调用委托
        var capturedBaseUrl = baseUrl ?? string.Empty;
        var capturedPath = path;
        var capturedMethod = method;
        var capturedHeaders = new Dictionary<string, string>(spec.Headers);
        var capturedParameters = parameters.ToList();
        var capturedHasBody = hasRequestBody;
        var capturedHttpClientFactory = _httpClientFactory;
        var capturedLogger = _logger;

        // 创建调用委托
        async Task<object?> InvokeAsync(AIFunctionArguments arguments, CancellationToken ct)
        {
            var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (arguments != null)
            {
                foreach (var kv in arguments)
                {
                    args[kv.Key] = kv.Value;
                }
            }

            // 每次调用创建新 HttpClient（由 IHttpClientFactory 管理池化）
            var httpClient = capturedHttpClientFactory.CreateClient("Tnzi.AI.OpenApi");
            return await ExecuteHttpCallAsync(
                httpClient,
                capturedBaseUrl,
                capturedPath,
                capturedMethod,
                capturedHeaders,
                capturedParameters,
                capturedHasBody,
                args,
                capturedLogger,
                ct);
        }

        // 使用 AIFunctionFactory.Create 创建 AIFunction
        var aiFunction = AIFunctionFactory.Create(
            InvokeAsync,
            new AIFunctionFactoryOptions
            {
                Name = fullToolName,
                Description = description
            });

        return aiFunction;
    }

    /// <summary>
    /// 执行 HTTP 调用
    /// </summary>
    public static async Task<string> ExecuteHttpCallAsync(
        HttpClient httpClient,
        string baseUrl,
        string path,
        string method,
        Dictionary<string, string> headers,
        List<OpenApiParameter> parameters,
        bool hasBody,
        IDictionary<string, object?> args,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // 构建 URL：替换路径参数、添加查询参数
            var resolvedPath = path;
            var queryParams = new List<string>();

            foreach (var param in parameters)
            {
                if (!args.TryGetValue(param.Name, out var value) || value == null)
                {
                    continue;
                }

                var valueStr = Convert.ToString(value) ?? string.Empty;

                switch (param.In)
                {
                    case "path":
                        resolvedPath = resolvedPath.Replace($"{{{param.Name}}}", Uri.EscapeDataString(valueStr));
                        break;
                    case "query":
                        queryParams.Add($"{Uri.EscapeDataString(param.Name)}={Uri.EscapeDataString(valueStr)}");
                        break;
                }
            }

            var url = $"{baseUrl.TrimEnd('/')}{resolvedPath}";
            if (queryParams.Count > 0)
            {
                url += "?" + string.Join("&", queryParams);
            }

            // 构建请求
            var request = new HttpRequestMessage(new HttpMethod(method), url);

            // 设置请求头
            foreach (var (key, headerValue) in headers)
            {
                request.Headers.TryAddWithoutValidation(key, headerValue);
            }

            // 设置请求体（仅对含 body 参数的操作）
            if (hasBody)
            {
                var bodyArgs = new Dictionary<string, object?>();
                foreach (var param in parameters.Where(p => p.In == "body"))
                {
                    if (args.TryGetValue(param.Name, out var value))
                    {
                        bodyArgs[param.Name] = value;
                    }
                }

                if (bodyArgs.Count > 0)
                {
                    var bodyJson = JsonSerializer.Serialize(bodyArgs);
                    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                }
            }

            var response = await httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return JsonSerializer.Serialize(new
                {
                    error = true,
                    statusCode = (int)response.StatusCode,
                    message = responseBody
                });
            }

            return responseBody;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "OpenAPI HTTP call failed: {Method} {Path}", method, path);
            return JsonSerializer.Serialize(new
            {
                error = true,
                message = $"HTTP call failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// 提取 OpenAPI 文档的基础 URL
    /// </summary>
    private static string? ExtractBaseUrl(JsonElement root)
    {
        if (root.TryGetProperty("servers", out var servers))
        {
            foreach (var server in servers.EnumerateArray())
            {
                if (server.TryGetProperty("url", out var url))
                {
                    return url.GetString();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 解析单个参数
    /// </summary>
    private static OpenApiParameter? ParseParameter(JsonElement param)
    {
        var name = param.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
        var location = param.TryGetProperty("in", out var inEl) ? inEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        // 仅支持 path 和 query 参数
        if (location != "path" && location != "query")
        {
            return null;
        }

        var required = param.TryGetProperty("required", out var reqEl) && reqEl.GetBoolean();
        var description = param.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;

        // 获取 schema
        var schema = param.TryGetProperty("schema", out var schemaEl)
            ? schemaEl
            : CreateDefaultStringSchema();

        return new OpenApiParameter
        {
            Name = name!,
            In = location!,
            Required = required,
            Description = description,
            Schema = schema
        };
    }

    /// <summary>
    /// 解析请求体为参数列表
    /// </summary>
    private static List<OpenApiParameter> ParseRequestBody(JsonElement requestBody)
    {
        var parameters = new List<OpenApiParameter>();

        if (!requestBody.TryGetProperty("content", out var content))
        {
            return parameters;
        }

        // 优先处理 application/json
        JsonElement schemaElement;
        if (content.TryGetProperty("application/json", out var jsonContent)
            && jsonContent.TryGetProperty("schema", out schemaElement))
        {
            // 如果 schema 是 object 类型且有 properties，提取每个属性作为参数
            if (schemaElement.TryGetProperty("properties", out var properties))
            {
                var requiredSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (schemaElement.TryGetProperty("required", out var requiredArray))
                {
                    foreach (var req in requiredArray.EnumerateArray())
                    {
                        var reqName = req.GetString();
                        if (reqName != null)
                        {
                            requiredSet.Add(reqName);
                        }
                    }
                }

                foreach (var prop in properties.EnumerateObject())
                {
                    var description = prop.Value.TryGetProperty("description", out var descEl)
                        ? descEl.GetString()
                        : null;

                    parameters.Add(new OpenApiParameter
                    {
                        Name = prop.Name,
                        In = "body",
                        Required = requiredSet.Contains(prop.Name),
                        Description = description,
                        Schema = prop.Value
                    });
                }
            }
            else
            {
                // 非 object schema，作为单一 body 参数
                parameters.Add(new OpenApiParameter
                {
                    Name = "body",
                    In = "body",
                    Required = requestBody.TryGetProperty("required", out var reqEl) && reqEl.GetBoolean(),
                    Description = "Request body",
                    Schema = schemaElement
                });
            }
        }

        return parameters;
    }

    /// <summary>
    /// 创建默认字符串 Schema
    /// </summary>
    private static JsonElement CreateDefaultStringSchema()
    {
        var json = JsonSerializer.Serialize(new { type = "string" });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// 构建工具描述
    /// </summary>
    private static string BuildDescription(JsonElement operation, string method, string path)
    {
        var parts = new List<string>();

        if (operation.TryGetProperty("summary", out var summary))
        {
            var summaryText = summary.GetString();
            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                parts.Add(summaryText);
            }
        }

        if (operation.TryGetProperty("description", out var desc))
        {
            var descText = desc.GetString();
            if (!string.IsNullOrWhiteSpace(descText))
            {
                parts.Add(descText);
            }
        }

        if (parts.Count == 0)
        {
            parts.Add($"{method} {path}");
        }

        return string.Join(" - ", parts);
    }

    /// <summary>
    /// 清理工具名称（移除非法字符）
    /// </summary>
    public static string SanitizeToolName(string name)
    {
        // 替换非字母数字和下划线的字符为下划线
        var sanitized = Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
        // 移除连续下划线
        sanitized = Regex.Replace(sanitized, @"_+", "_");
        // 移除首尾下划线
        return sanitized.Trim('_');
    }

    /// <summary>
    /// 从方法和路径生成工具名称
    /// </summary>
    public static string GenerateToolName(string method, string path)
    {
        // 将路径中的 / 和 {} 转换为有意义的名称
        var pathPart = path
            .Replace("/", "_")
            .Replace("{", "by_")
            .Replace("}", "");

        return SanitizeToolName($"{method.ToLowerInvariant()}{pathPart}");
    }

    /// <summary>
    /// 判断是否为标准 HTTP 方法
    /// </summary>
    private static bool IsHttpMethod(string method)
    {
        return method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS";
    }

    /// <summary>
    /// 判断操作是否应被包含
    /// </summary>
    public static bool ShouldIncludeOperation(
        string? operationId,
        HashSet<string>? includeSet,
        HashSet<string>? excludeSet)
    {
        // IncludeOperations 优先：若配置了 Include 列表，只包含列表中的操作
        if (includeSet is { Count: > 0 })
        {
            return !string.IsNullOrWhiteSpace(operationId) && includeSet.Contains(operationId);
        }

        // ExcludeOperations：若配置了 Exclude 列表，排除列表中的操作
        if (excludeSet is { Count: > 0 })
        {
            return string.IsNullOrWhiteSpace(operationId) || !excludeSet.Contains(operationId);
        }

        // 未配置过滤条件，包含所有操作
        return true;
    }
}

/// <summary>
/// OpenAPI 参数模型
/// </summary>
[ExperimentalApi(Reason = "OpenAPI tool generation is evolving")]
public class OpenApiParameter
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 参数位置：path、query、body
    /// </summary>
    public string In { get; set; } = string.Empty;

    /// <summary>
    /// 是否必填
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// 参数描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 参数 JSON Schema
    /// </summary>
    public JsonElement Schema { get; set; }
}
