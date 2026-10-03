using System.Net;
using System.Net.Http.Headers;
using Tnzi.AI.Extensions;

namespace Tnzi.Tests.AI;

/// <summary>
/// 核心 <see cref="IAiUtility"/> 默认实现的单元测试。
/// </summary>
/// <remarks>
/// 这些用例守着「不加载 Tnzi.AI 也能调 LLM」这条能力：请求形状（端点拼接、鉴权头、
/// 消息体）、配置解析（提供商 / 模型别名 / 覆盖优先级）、以及失败时的重试判据。
/// </remarks>
public class OpenAiCompatibleAiUtilityTests
{
    private const string ProviderName = "test";

    // ------------------------------------------------------------------
    // 配置缺失 —— 一律返回 null 且不发出任何请求
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoProvidersConfigured_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var sut = CreateSut(handler, new AiProviderRegistryOptions());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderDisabled_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry(p => p.Enabled = false);
        var sut = CreateSut(handler, registry);

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultProviderNotInDictionary_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry();
        registry.DefaultProvider = "missing";
        var sut = CreateSut(handler, registry);

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_NoApiKey_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry(p => p.ApiKey = null);
        var sut = CreateSut(handler, registry);

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_NoModelAnywhere_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry(p => p.DefaultModel = null);
        var sut = CreateSut(handler, registry, new AiUtilityOptions { Model = null });

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_NonHttpBaseUrl_ReturnsNullWithoutRequest()
    {
        var handler = new RecordingHandler();
        var registry = Registry(p => p.BaseUrl = "ftp://example.com/v1");
        var sut = CreateSut(handler, registry);

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    // ------------------------------------------------------------------
    // IsAvailable —— 调用方据此决定要不要把 AI 入口显示出来
    // ------------------------------------------------------------------

    [Fact]
    public void IsAvailable_EnabledProviderWithApiKey_IsTrue()
    {
        Assert.True(CreateSut(new RecordingHandler(), Registry()).IsAvailable);
    }

    [Fact]
    public void IsAvailable_NoProvidersConfigured_IsFalse()
    {
        Assert.False(CreateSut(new RecordingHandler(), new AiProviderRegistryOptions()).IsAvailable);
    }

    [Fact]
    public void IsAvailable_ProviderDisabled_IsFalse()
    {
        Assert.False(CreateSut(new RecordingHandler(), Registry(p => p.Enabled = false)).IsAvailable);
    }

    [Fact]
    public void IsAvailable_DefaultProviderNotInDictionary_IsFalse()
    {
        var registry = Registry();
        registry.DefaultProvider = "missing";

        Assert.False(CreateSut(new RecordingHandler(), registry).IsAvailable);
    }

    [Fact]
    public void IsAvailable_ProviderWithoutApiKey_IsFalse()
    {
        // 「已启用但没配 key」正是那种「按钮显示出来、点了没反应」的形态。
        Assert.False(CreateSut(new RecordingHandler(), Registry(p => p.ApiKey = null)).IsAvailable);
    }

    [Fact]
    public void IsAvailable_ReflectsOptionsChange_WithoutRecreatingTheService()
    {
        // 不缓存探测结果：配置热更新后必须立刻反映，否则 admin 改了配置还要重启才能看到入口。
        var registry = Registry();
        var monitor = new MutableOptionsMonitor<AiProviderRegistryOptions>(registry);
        var services = new ServiceCollection();
        services.AddHttpClient(AiUtilityHttpClientNames.Fallback);
        var sut = new OpenAiCompatibleAiUtility(
            services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            monitor,
            new StaticOptionsMonitor<AiUtilityOptions>(new AiUtilityOptions()));

        Assert.True(sut.IsAvailable);

        monitor.Set(new AiProviderRegistryOptions());

        Assert.False(sut.IsAvailable);
    }

    // ------------------------------------------------------------------
    // 请求形状
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsTrimmedContent()
    {
        var handler = new RecordingHandler(Completion("  hello world  "));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task ExecuteAsync_PostsToChatCompletionsUnderConfiguredBaseUrl()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.BaseUrl = "https://api.deepseek.com/v1"));

        await sut.ExecuteAsync("system", "user");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.deepseek.com/v1/chat/completions", request.Uri.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_TrailingSlashInBaseUrl_DoesNotDoubleUpSeparator()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.BaseUrl = "https://api.deepseek.com/v1/"));

        await sut.ExecuteAsync("system", "user");

        Assert.Equal("https://api.deepseek.com/v1/chat/completions", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_NoBaseUrl_FallsBackToOfficialOpenAiEndpoint()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.BaseUrl = null));

        await sut.ExecuteAsync("system", "user");

        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_SendsBearerAuthorizationHeader()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.ApiKey = "sk-secret"));

        await sut.ExecuteAsync("system", "user");

        Assert.Equal("Bearer sk-secret", handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task ExecuteAsync_SendsSystemThenUserMessage()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry());

        await sut.ExecuteAsync("you are a helper", "what is 2+2?");

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        var messages = body.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("you are a helper", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("what is 2+2?", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_SendsSnakeCaseMaxTokens()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions { MaxTokens = 321, Temperature = 0.7 });

        await sut.ExecuteAsync("system", "user");

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(321, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.7, body.RootElement.GetProperty("temperature").GetDouble(), 3);
    }

    // ------------------------------------------------------------------
    // 模型与覆盖优先级
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_UsesProviderDefaultModel()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(p => p.DefaultModel = "gpt-4.1-mini"), new AiUtilityOptions { Model = null });

        await sut.ExecuteAsync("system", "user");

        Assert.Equal("gpt-4.1-mini", ModelOf(handler.Requests[0]));
    }

    [Fact]
    public async Task ExecuteAsync_CallOptionsModelOverridesUtilityDefault()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions { Model = "from-options" });

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Model = "from-call" });

        Assert.Equal("from-call", ModelOf(handler.Requests[0]));
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesModelAlias()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var registry = Registry(p => p.Models = new Dictionary<string, string> { ["fast"] = "gpt-4.1-mini" });
        var sut = CreateSut(handler, registry);

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Model = "fast" });

        Assert.Equal("gpt-4.1-mini", ModelOf(handler.Requests[0]));
    }

    [Fact]
    public async Task ExecuteAsync_CallOptionsOverrideTokensAndTemperature()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions { MaxTokens = 100, Temperature = 0.3 });

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { MaxTokens = 7, Temperature = 1.5 });

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(7, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(1.5, body.RootElement.GetProperty("temperature").GetDouble(), 3);
    }

    // ------------------------------------------------------------------
    // 输出预算 —— 默认值不能按最窄的调用点定
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_DefaultMaxTokens_LeavesRoomForAnAnswer()
    {
        // 四个框架内消费者（RAG 问答、知识图谱抽取、Agent 评估、建议生成）都吃这个默认值
        // 且都不传覆盖。按标题长度定默认值会把它们的输出静默截断 —— 图谱抽取期望完整 JSON，
        // 截断后解析失败，产出零实体且不报错。
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions());

        await sut.ExecuteAsync("system", "user");

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.True(body.RootElement.GetProperty("max_tokens").GetInt32() >= 1024,
            "the utility default must be usable for question answering, not sized for a title");
    }

    [Fact]
    public async Task GenerateTitleAsync_AsksForATitleSizedBudget()
    {
        // 标题是那个「窄」的调用点，所以由它显式传小值，而不是让所有人共用一个小默认值
        var handler = new RecordingHandler(Completion("A short title"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions());

        await sut.GenerateTitleAsync("a long conversation transcript", maxLength: 50);

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        var maxTokens = body.RootElement.GetProperty("max_tokens").GetInt32();
        Assert.True(maxTokens > 0 && maxTokens <= 256,
            $"a title call should ask for a small budget, got {maxTokens}");
        Assert.True(maxTokens < new AiUtilityOptions().MaxTokens,
            "the title budget must be tighter than the shared default");
    }

    [Fact]
    public async Task GenerateTitleAsync_KeepsAnExplicitCallerBudget()
    {
        var handler = new RecordingHandler(Completion("A short title"));
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions());

        await sut.GenerateTitleAsync("transcript", maxLength: 50,
            options: new AiUtilityCallOptions { MaxTokens = 512, Model = "from-call" });

        using var body = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(512, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("from-call", ModelOf(handler.Requests[0]));
    }

    // ------------------------------------------------------------------
    // 重试判据
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RateLimitedThenSuccess_RetriesAndReturnsContent()
    {
        var handler = new RecordingHandler(
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            () => Completion("recovered"));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Equal("recovered", result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ServerErrorThenSuccess_Retries()
    {
        var handler = new RecordingHandler(
            () => new HttpResponseMessage(HttpStatusCode.BadGateway),
            () => Completion("recovered"));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Equal("recovered", result);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_PersistentServerError_StopsAfterRetryBudget()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        // 首次尝试 + 2 次重试
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ClientError_DoesNotRetry()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_Unauthorized_DoesNotRetry()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Null(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_TransportFailureThenSuccess_Retries()
    {
        var attempts = 0;
        var handler = new RecordingHandler(() =>
        {
            attempts++;
            if (attempts == 1) throw new HttpRequestException("connection reset");
            return Completion("recovered");
        });
        var sut = CreateSut(handler, Registry());

        var result = await sut.ExecuteAsync("system", "user");

        Assert.Equal("recovered", result);
        Assert.Equal(2, attempts);
    }

    // ------------------------------------------------------------------
    // 响应解析
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ResponseWithoutChoices_ReturnsNull()
    {
        var handler = new RecordingHandler(Json("""{"id":"x","choices":[]}"""));
        var sut = CreateSut(handler, Registry());

        Assert.Null(await sut.ExecuteAsync("system", "user"));
    }

    [Fact]
    public async Task ExecuteAsync_ChoiceWithoutTextContent_ReturnsNull()
    {
        var handler = new RecordingHandler(Json("""{"choices":[{"message":{"reasoning_content":"thinking"}}]}"""));
        var sut = CreateSut(handler, Registry());

        Assert.Null(await sut.ExecuteAsync("system", "user"));
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceOnlyContent_ReturnsNull()
    {
        var handler = new RecordingHandler(Completion("   "));
        var sut = CreateSut(handler, Registry());

        Assert.Null(await sut.ExecuteAsync("system", "user"));
    }

    [Fact]
    public async Task ExecuteAsync_NonJsonResponse_ReturnsNull()
    {
        var handler = new RecordingHandler(Json("<html>gateway error</html>"));
        var sut = CreateSut(handler, Registry());

        Assert.Null(await sut.ExecuteAsync("system", "user"));
    }

    // ------------------------------------------------------------------
    // 取消
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var handler = new RecordingHandler(() =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var sut = CreateSut(handler, Registry());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.ExecuteAsync("system", "user", cancellationToken: cts.Token));
    }

    // ------------------------------------------------------------------
    // 参数校验
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_BlankSystemPrompt_Throws(string systemPrompt)
    {
        var sut = CreateSut(new RecordingHandler(), Registry());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sut.ExecuteAsync(systemPrompt, "user"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_BlankUserMessage_Throws(string userMessage)
    {
        var sut = CreateSut(new RecordingHandler(), Registry());

        await Assert.ThrowsAnyAsync<ArgumentException>(() => sut.ExecuteAsync("system", userMessage));
    }

    // ------------------------------------------------------------------
    // 调用方自带提供商（AiUtilityCallOptions.Provider）
    // ------------------------------------------------------------------

    private const string InlineKey = "sk-inline-secret-0123456789";

    private static AiUtilityInlineProvider Inline(
        string baseUrl = "https://vendor.example.net/v1",
        string apiKey = InlineKey,
        string? defaultModel = "vendor-model",
        int? timeoutSeconds = null)
        => new() { BaseUrl = baseUrl, ApiKey = apiKey, DefaultModel = defaultModel, TimeoutSeconds = timeoutSeconds };

    [Fact]
    public async Task Inline_SendsToSuppliedBaseUrlWithSuppliedKeyAndModel_WithoutAnyConfiguredProvider()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, new AiProviderRegistryOptions());

        var result = await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = Inline(baseUrl: "https://vendor.example.net/v1/") });

        Assert.Equal("ok", result);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://vendor.example.net/v1/chat/completions", request.Uri.ToString());
        Assert.Equal("Bearer " + InlineKey, request.Authorization);
        Assert.Equal("vendor-model", ModelOf(request));
    }

    [Fact]
    public async Task Inline_TakesPrecedenceOverConfiguredProvider()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry());

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = Inline() });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("vendor.example.net", request.Uri.Host);
        Assert.Equal("Bearer " + InlineKey, request.Authorization);
    }

    [Fact]
    public async Task Inline_CallModelOverridesInlineDefaultModel()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, new AiProviderRegistryOptions());

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Model = "vendor-large", Provider = Inline() });

        Assert.Equal("vendor-large", ModelOf(Assert.Single(handler.Requests)));
    }

    [Fact]
    public async Task Inline_IgnoresUtilityModelAndConfiguredAliases()
    {
        // AI:Utility:Model 与别名字典是给配置提供商定的名字，发给另一家只会得到 model_not_found。
        var handler = new RecordingHandler(Completion("ok"));
        var registry = Registry(p => p.Models = new Dictionary<string, string> { ["fast"] = "configured-fast" });
        var sut = CreateSut(handler, registry, new AiUtilityOptions { Model = "configured-utility-model" });

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = Inline() });
        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Model = "fast", Provider = Inline() });

        Assert.Equal(["vendor-model", "fast"], handler.Requests.Select(ModelOf));
    }

    [Fact]
    public async Task Inline_UtilityDefaultsStillApplyForMaxTokensAndTemperature()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, new AiProviderRegistryOptions(), new AiUtilityOptions { MaxTokens = 321, Temperature = 0.7 });

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = Inline() });

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal(321, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.7, body.RootElement.GetProperty("temperature").GetDouble());
    }

    public static TheoryData<string, AiUtilityInlineProvider> InvalidInlineProviders => new()
    {
        { "blank BaseUrl", Inline(baseUrl: "") },
        { "whitespace BaseUrl", Inline(baseUrl: "   ") },
        { "ftp BaseUrl", Inline(baseUrl: "ftp://vendor.example.net/v1") },
        { "relative BaseUrl", Inline(baseUrl: "vendor.example.net/v1") },
        // 把密钥误填进地址栏：日志里不能因为回显地址而出现密钥
        { "key typed into BaseUrl", Inline(baseUrl: InlineKey) },
        { "blank ApiKey", Inline(apiKey: "") },
        { "whitespace ApiKey", Inline(apiKey: "   ") },
        { "zero timeout", Inline(timeoutSeconds: 0) },
        { "timeout above 600", Inline(timeoutSeconds: 601) },
    };

    [Theory]
    [MemberData(nameof(InvalidInlineProviders))]
    public async Task Inline_Invalid_ReturnsNullWithWarning_NoRequest_KeyNotLogged(string _, AiUtilityInlineProvider inline)
    {
        var handler = new RecordingHandler(Completion("ok"));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, Registry(), logger: logger);

        var result = await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = inline });

        Assert.Null(result);
        Assert.Empty(handler.Requests);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
        logger.AssertNeverContains(InlineKey);
    }

    [Fact]
    public async Task Inline_NoModelAnywhere_ReturnsNullWithWarning_NoRequest()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, Registry(), new AiUtilityOptions { Model = "configured-utility-model" }, logger);

        var result = await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = Inline(defaultModel: null) });

        Assert.Null(result);
        Assert.Empty(handler.Requests);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Inline_FailureResponseEchoingTheKey_IsRedactedInLogs()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent($"{{\"error\":\"Incorrect API key provided: {InlineKey}\"}}", Encoding.UTF8, "application/json")
        });
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, new AiProviderRegistryOptions(), logger: logger);

        var result = await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = Inline() });

        Assert.Null(result);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains("inline:vendor.example.net")
            && e.Message.Contains("***"));
        logger.AssertNeverContains(InlineKey);
    }

    [Fact]
    public async Task Inline_TransportFailure_KeyNotLogged()
    {
        var handler = new RecordingHandler(() => throw new HttpRequestException($"connection refused while sending Bearer {InlineKey}"));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, new AiProviderRegistryOptions(), logger: logger);

        var result = await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = Inline() });

        Assert.Null(result);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains("***"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("***"));
        logger.AssertNeverContains(InlineKey);
    }

    [Fact]
    public void Inline_ToString_DoesNotExposeTheKey()
    {
        var text = Inline().ToString();

        Assert.DoesNotContain(InlineKey, text);
        Assert.Contains("vendor.example.net", text);
    }

    [Fact]
    public async Task Inline_GenerateTitleAsync_KeepsTheSuppliedProvider()
    {
        // GenerateTitleAsync 在调用方没给 MaxTokens 时会重建选项；重建时漏掉 Provider 会把请求发去配置提供商。
        var handler = new RecordingHandler(Completion("A title"));
        var sut = CreateSut(handler, Registry());

        var title = await sut.GenerateTitleAsync("User: hi", options: new AiUtilityCallOptions { Provider = Inline() });

        Assert.Equal("A title", title);
        Assert.Equal("vendor.example.net", Assert.Single(handler.Requests).Uri.Host);
    }

    [Fact]
    public async Task NoInlineProvider_ConfiguredPathUnchanged()
    {
        var handler = new RecordingHandler(Completion("ok"));
        var sut = CreateSut(handler, Registry());

        await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Model = "explicit" });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.example.com/v1/chat/completions", request.Uri.ToString());
        Assert.Equal("Bearer sk-test-key", request.Authorization);
        Assert.Equal("explicit", ModelOf(request));
    }

    // ------------------------------------------------------------------
    // 每次尝试的超时由 TimeoutSeconds 决定（不被 HttpClient 默认的 100 秒截断）
    // ------------------------------------------------------------------

    [Fact]
    public async Task Inline_TimeoutAbove100Seconds_IsNotCutAt100()
    {
        var time = new ManualTimeProvider();
        var handler = new BlockingHandler();
        var sut = CreateSut(handler, new AiProviderRegistryOptions(), timeProvider: time);
        using var caller = new CancellationTokenSource();

        var call = sut.ExecuteAsync("system", "user",
            new AiUtilityCallOptions { Provider = Inline(timeoutSeconds: 150) }, caller.Token);
        var attemptToken = await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        time.Advance(TimeSpan.FromSeconds(101));
        Assert.False(attemptToken.IsCancellationRequested, "a 150 s timeout must still be running at 101 s");

        time.Advance(TimeSpan.FromSeconds(50));
        Assert.True(attemptToken.IsCancellationRequested, "the attempt must be cancelled once 150 s have passed");

        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    /// <summary>
    /// 退避等待与 Retry-After 的日期换算都走注入的时钟：Retry-After 给的是「注入时钟的现在 + 8 秒」，
    /// 等待必须恰好是 8 秒、且只在时钟推进过去之后才发第二次请求。
    /// </summary>
    [Fact]
    public async Task RetryAfterDate_IsMeasuredAndWaitedOnTheInjectedClock()
    {
        var time = new ManualTimeProvider();
        var retryAt = time.GetUtcNow() + TimeSpan.FromSeconds(8);
        var handler = new RecordingHandler(
            () =>
            {
                var busy = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                busy.Headers.RetryAfter = new RetryConditionHeaderValue(retryAt);
                return busy;
            },
            () => Completion("after wait"));
        var sut = CreateSut(handler, Registry(), timeProvider: time);

        var call = sut.ExecuteAsync("system", "user");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!RequestedEightSecondTimer(time) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        Assert.True(RequestedEightSecondTimer(time), "the retry wait must be an 8 s timer on the injected clock");
        Assert.Single(handler.Requests);

        time.Advance(TimeSpan.FromSeconds(8));

        Assert.Equal("after wait", await call.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, handler.Requests.Count);

        static bool RequestedEightSecondTimer(ManualTimeProvider clock)
        {
            lock (clock.RequestedDueTimes)
            {
                return clock.RequestedDueTimes.Contains(TimeSpan.FromSeconds(8));
            }
        }
    }

    [Fact]
    public async Task NoTimeoutSeconds_AttemptStillTimesOutAt100Seconds()
    {
        // 未设置 TimeoutSeconds 的部署此前实际生效的是 HttpClient 默认的 100 秒；客户端超时关掉后必须原样保留。
        var time = new ManualTimeProvider();
        var handler = new BlockingHandler();
        var sut = CreateSut(handler, Registry(), timeProvider: time);
        using var caller = new CancellationTokenSource();

        var call = sut.ExecuteAsync("system", "user", cancellationToken: caller.Token);
        var attemptToken = await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        time.Advance(TimeSpan.FromSeconds(99));
        Assert.False(attemptToken.IsCancellationRequested);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(attemptToken.IsCancellationRequested);

        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
    }

    [Fact]
    public async Task Inline_UnexpectedExceptionEchoingTheKey_IsRedactedInLogs()
    {
        // 走 ExecuteAsync 的兜底 catch（非 HttpRequestException，不重试）：异常全文进日志前必须抹掉密钥。
        var handler = new RecordingHandler(() => throw new InvalidOperationException($"proxy rejected Authorization: Bearer {InlineKey}"));
        var logger = new CapturingLogger();
        var sut = CreateSut(handler, new AiProviderRegistryOptions(), logger: logger);

        var result = await sut.ExecuteAsync("system", "user", new AiUtilityCallOptions { Provider = Inline() });

        Assert.Null(result);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains(nameof(InvalidOperationException))
            && e.Message.Contains("***"));
        logger.AssertNeverContains(InlineKey);
    }

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------

    private static AiProviderRegistryOptions Registry(Action<AiProviderOptions>? configure = null)
    {
        var provider = new AiProviderOptions
        {
            Name = ProviderName,
            Enabled = true,
            ApiKey = "sk-test-key",
            BaseUrl = "https://api.example.com/v1",
            DefaultModel = "test-model"
        };
        configure?.Invoke(provider);

        return new AiProviderRegistryOptions
        {
            DefaultProvider = ProviderName,
            Providers = new Dictionary<string, AiProviderOptions> { [ProviderName] = provider }
        };
    }

    private static OpenAiCompatibleAiUtility CreateSut(
        HttpMessageHandler handler,
        AiProviderRegistryOptions registry,
        AiUtilityOptions? utility = null,
        CapturingLogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(AiUtilityHttpClientNames.For(ProviderName))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(AiUtilityHttpClientNames.For("missing"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(AiUtilityHttpClientNames.Fallback)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddHttpClient(AiUtilityHttpClientNames.Inline)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();

        return new OpenAiCompatibleAiUtility(
            provider.GetRequiredService<IHttpClientFactory>(),
            new StaticOptionsMonitor<AiProviderRegistryOptions>(registry),
            new StaticOptionsMonitor<AiUtilityOptions>(utility ?? new AiUtilityOptions()),
            logger,
            timeProvider);
    }

    private static string ModelOf(CapturedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body);
        return body.RootElement.GetProperty("model").GetString()!;
    }

    private static HttpResponseMessage Completion(string content)
        => Json(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content } } }
        }));

    private static HttpResponseMessage Json(string payload)
        => new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(Uri Uri, HttpMethod Method, string? Authorization, string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _queued = new();
        private readonly Func<HttpResponseMessage>? _repeating;

        public List<CapturedRequest> Requests { get; } = [];

        /// <summary>永远返回 200 空 completion（用于「不该发请求」的断言）。</summary>
        public RecordingHandler() => _repeating = () => Completion(string.Empty);

        /// <summary>固定响应，可被重复取用。</summary>
        public RecordingHandler(HttpResponseMessage response) => _repeating = () => Clone(response);

        /// <summary>每次调用都执行同一个工厂（可抛异常，用于传输层失败）。</summary>
        public RecordingHandler(Func<HttpResponseMessage> factory) => _repeating = factory;

        /// <summary>按顺序返回，用尽后重复最后一个。</summary>
        public RecordingHandler(params Func<HttpResponseMessage>[] sequence)
        {
            foreach (var item in sequence)
            {
                _queued.Enqueue(item);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Method,
                request.Headers.TryGetValues("Authorization", out var values) ? string.Join(' ', values) : null,
                body));

            var factory = _queued.Count > 0 ? _queued.Dequeue() : _repeating;
            return factory!();
        }

        private static HttpResponseMessage Clone(HttpResponseMessage source)
            => new(source.StatusCode)
            {
                Content = new StringContent(
                    source.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                    Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; private set; } = value;

        public void Set(T next) => CurrentValue = next;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>请求进来后一直挂着，直到本次尝试的令牌被取消；把那个令牌交给测试观察。</summary>
    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource<CancellationToken> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>手动推进的时钟：计时器只在 <see cref="Advance"/> 越过到期时刻时同步触发。</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Epoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> _timers = [];
        private TimeSpan _now;

        /// <summary>每次创建计时器时请求的到期时长（按创建顺序）。</summary>
        public List<TimeSpan> RequestedDueTimes { get; } = [];

        public override DateTimeOffset GetUtcNow()
        {
            lock (_timers)
            {
                return Epoch + _now;
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (RequestedDueTimes)
            {
                RequestedDueTimes.Add(dueTime);
            }

            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_timers)
            {
                _timers.Add(timer);
            }
            return timer;
        }

        public void Advance(TimeSpan delta)
        {
            List<ManualTimer> due;
            lock (_timers)
            {
                _now += delta;
                due = _timers.Where(t => t.DueAt is { } at && at <= _now).ToList();
                foreach (var timer in due)
                {
                    timer.DueAt = null;
                }
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public TimeSpan? DueAt { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._timers)
                {
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                }
                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (owner._timers)
                {
                    DueAt = null;
                    owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, string? Exception);

    /// <summary>记下每一条日志的渲染文本与异常全文 —— 断言密钥不在任何一处。</summary>
    private sealed class CapturingLogger : ILogger<OpenAiCompatibleAiUtility>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception?.ToString()));

        public void AssertNeverContains(string secret)
        {
            foreach (var entry in Entries)
            {
                Assert.DoesNotContain(secret, entry.Message);
                Assert.DoesNotContain(secret, entry.Exception ?? string.Empty);
            }
        }
    }
}
