
namespace Tnzi.AI.Services;

/// <summary>
/// 结构化输出服务实现
/// </summary>
/// <remarks>
/// <para>
/// 使用 Microsoft.Extensions.AI 的 JSON Schema 支持实现结构化输出。
/// 支持自动 Schema 生成、严格模式验证和重试机制。
/// </para>
/// </remarks>
public class StructuredOutputService : ApplicationService, IStructuredOutputService
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IAgentFactory _agentFactory;
    private readonly IOptions<AIOptions> _options;

    private const string DefaultSystemPrompt = """
        You are a helpful assistant that outputs responses in JSON format.
        Always output valid JSON that matches the requested schema.
        Do not include any explanations, markdown formatting, or additional text.
        Output only the JSON object.
        """;

    public StructuredOutputService(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        IAgentFactory agentFactory,
        IOptions<AIOptions> options)
        : base(serviceProvider)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _agentFactory = Check.NotNull(agentFactory);
        _options = Check.NotNull(options);
    }

    /// <inheritdoc />
    public async Task<Result<T>> GetStructuredOutputAsync<T>(
        string prompt,
        StructuredOutputOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        Check.NotNullOrEmpty(prompt);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, prompt)
        };

        return await GetStructuredOutputAsync<T>(messages, options, ct);
    }

    /// <inheritdoc />
    public async Task<Result<T>> GetStructuredOutputAsync<T>(
        IEnumerable<ChatMessage> messages,
        StructuredOutputOptions? options = null,
        CancellationToken ct = default) where T : class
    {
        Check.NotNull(messages);

        options ??= new StructuredOutputOptions();
        var retryCount = 0;
        string? lastError = null;

        while (retryCount <= options.MaxRetries)
        {
            try
            {
                var result = await TryGetStructuredOutputAsync<T>(messages, options, retryCount > 0, ct);

                if (result.Succeeded)
                {
                    if (retryCount > 0)
                    {
                        Logger.LogDebug("Structured output succeeded after {RetryCount} retries", retryCount);
                    }
                    return result;
                }

                lastError = result.Message;
                retryCount++;

                if (retryCount <= options.MaxRetries)
                {
                    Logger.LogDebug(
                        "Structured output parsing failed, retrying ({RetryCount}/{MaxRetries}): {Error}",
                        retryCount, options.MaxRetries, result.Message);
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                retryCount++;

                if (retryCount <= options.MaxRetries)
                {
                    Logger.LogDebug(ex,
                        "Structured output request failed, retrying ({RetryCount}/{MaxRetries})",
                        retryCount, options.MaxRetries);
                }
            }
        }

        Logger.LogWarning(
            "Failed to get structured output after {RetryCount} attempts: {Error}",
            retryCount, lastError);

        return Fail<T>(lastError ?? "Failed to get structured output", 500);
    }

    /// <summary>
    /// 尝试获取结构化输出
    /// </summary>
    private async Task<Result<T>> TryGetStructuredOutputAsync<T>(
        IEnumerable<ChatMessage> messages,
        StructuredOutputOptions options,
        bool isRetry,
        CancellationToken ct) where T : class
    {
        var providerName = options.Provider ?? _options.Value.DefaultProvider;

        // 构建消息列表
        var messageList = new List<ChatMessage>();

        // 添加系统提示词
        var systemPrompt = options.SystemPrompt ?? DefaultSystemPrompt;
        messageList.Add(new ChatMessage(ChatRole.System, systemPrompt));

        // 添加用户消息
        messageList.AddRange(messages);

        // 如果是重试，添加更强的格式要求
        if (isRetry)
        {
            messageList.Add(new ChatMessage(
                ChatRole.System,
                "IMPORTANT: Your previous response was not valid JSON. Please output ONLY valid JSON, no markdown, no explanations."));
        }

        if (options.UseAgent)
        {
            return await TryGetStructuredOutputViaAgentAsync<T>(messageList, providerName, options, ct);
        }

        return await TryGetStructuredOutputViaChatClientAsync<T>(messageList, providerName, options, ct);
    }

    /// <summary>
    /// 通过 AgentExecutor 获取结构化输出（可复用 ContextProviders/Tools）
    /// </summary>
    private async Task<Result<T>> TryGetStructuredOutputViaAgentAsync<T>(
        List<ChatMessage> messageList,
        string providerName,
        StructuredOutputOptions options,
        CancellationToken ct) where T : class
    {
        try
        {
            var temperature = options.Temperature.HasValue ? (double)options.Temperature.Value : (double?)null;
            var maxTokens = options.MaxOutputTokens;
            var agent = await _agentFactory.CreateAgentAsync(
                providerName,
                options.ModelId,
                instructions: null,
                name: null,
                toolGroups: null,
                temperature,
                maxTokens,
                options: null,
                ct);

            // 通过 AgentExecutor 执行，获取文本响应后手动反序列化
            var response = await agent.ExecuteAsync(messageList, ct);
            var rawResponse = response.Text ?? string.Empty;

            // 从文本解析 JSON
            var cleanedResponse = CleanJsonResponse(rawResponse);
            var data = JsonSerializer.Deserialize<T>(cleanedResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data != null)
            {
                Logger.LogDebug("Structured output via agent succeeded, raw response length: {Length}", rawResponse.Length);
                return Ok(data);
            }

            return Fail<T>("Deserialization returned null", 500);
        }
        catch (JsonException ex)
        {
            return Fail<T>($"JSON parsing error: {ex.Message}", 500);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Structured output via agent failed");
            return Fail<T>(ex.Message, 500);
        }
    }

    /// <summary>
    /// 通过 IChatClient 直接获取结构化输出（轻量模式）
    /// </summary>
    private async Task<Result<T>> TryGetStructuredOutputViaChatClientAsync<T>(
        List<ChatMessage> messageList,
        string providerName,
        StructuredOutputOptions options,
        CancellationToken ct) where T : class
    {
        var openAiChatClient = _chatClientFactory.GetChatClient(providerName, options.ModelId);
        var chatClient = openAiChatClient.AsIChatClient();

        var chatOptions = new ChatOptions();

        if (!string.IsNullOrEmpty(options.ModelId))
        {
            chatOptions.ModelId = options.ModelId;
        }

        if (options.Temperature.HasValue)
        {
            chatOptions.Temperature = options.Temperature.Value;
        }

        if (options.StrictMode)
        {
            chatOptions.ResponseFormat = CreateJsonSchemaResponseFormat<T>(options);
        }
        else
        {
            chatOptions.ResponseFormat = ChatResponseFormat.Json;
        }

        var response = await chatClient.GetResponseAsync(messageList, chatOptions, ct);
        var rawResponse = response.Text ?? string.Empty;

        try
        {
            var cleanedResponse = CleanJsonResponse(rawResponse);

            var data = JsonSerializer.Deserialize<T>(cleanedResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (data != null)
            {
                Logger.LogDebug("Structured output via chat client succeeded, raw response length: {Length}", rawResponse.Length);
                return Ok(data);
            }

            return Fail<T>("Deserialization returned null", 500);
        }
        catch (JsonException ex)
        {
            Logger.LogDebug("JSON parsing failed, raw response: {RawResponse}", rawResponse);
            return Fail<T>($"JSON parsing error: {ex.Message}", 500);
        }
    }

    /// <summary>
    /// 创建 JSON Schema 响应格式
    /// </summary>
    private static ChatResponseFormatJson CreateJsonSchemaResponseFormat<T>(StructuredOutputOptions options)
    {
        // 如果提供了自定义 Schema，使用自定义 Schema
        if (!string.IsNullOrEmpty(options.JsonSchema))
        {
            var schemaElement = JsonSerializer.Deserialize<JsonElement>(options.JsonSchema);
            return ChatResponseFormat.ForJsonSchema(schemaElement, typeof(T).Name);
        }

        // 使用类型名称生成简单的 JSON Schema
        // 更完整的 Schema 生成可以使用 JsonSchemaExporter（需要额外依赖）
        var schema = GenerateSimpleJsonSchema<T>();
        return ChatResponseFormat.ForJsonSchema(schema, typeof(T).Name);
    }

    /// <summary>
    /// 生成简单的 JSON Schema（基于类型的公共属性）
    /// </summary>
    private static JsonElement GenerateSimpleJsonSchema<T>()
    {
        var type = typeof(T);
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;

            var propSchema = GetPropertySchema(prop.PropertyType);
            properties[prop.Name] = propSchema;

            // 非可空类型标记为必需
            if (!IsNullable(prop.PropertyType))
            {
                required.Add(prop.Name);
            }
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        var jsonString = JsonSerializer.Serialize(schema);
        return JsonSerializer.Deserialize<JsonElement>(jsonString);
    }

    /// <summary>
    /// 获取属性的 JSON Schema 类型
    /// </summary>
    private static Dictionary<string, object> GetPropertySchema(Type type)
    {
        // 处理可空类型
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        var jsonType = underlyingType switch
        {
            Type t when t == typeof(string) => "string",
            Type t when t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) => "integer",
            Type t when t == typeof(decimal) || t == typeof(double) || t == typeof(float) => "number",
            Type t when t == typeof(bool) => "boolean",
            Type t when t == typeof(DateTime) || t == typeof(DateTimeOffset) => "string",
            Type t when t == typeof(Guid) => "string",
            Type t when t.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(t) && t != typeof(string) => "array",
            _ => "object"
        };

        return new Dictionary<string, object> { ["type"] = jsonType };
    }

    /// <summary>
    /// 检查类型是否可空
    /// </summary>
    private static bool IsNullable(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }

    /// <summary>
    /// 清理 JSON 响应
    /// </summary>
    private static string CleanJsonResponse(string response)
    {
        var trimmed = response.Trim();

        // 移除 markdown 代码块标记
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed.Substring(3);
        }

        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        }

        return trimmed.Trim();
    }
}
