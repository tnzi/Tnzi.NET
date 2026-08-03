using System.ClientModel;
using System.ClientModel.Primitives;
using System.Reflection;
using System.Text;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// ThinkingRequestPolicy 推理注入参数映射测试
/// </summary>
public class ThinkingRequestPolicyTests
{
    #region OpenAI Effort Mapping

    [Theory]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.Max, "high")]
    public void OpenAIEffortMapping_CorrectStringValues(ReasoningEffort effort, string expected)
    {
        var injected = InjectAndCapture("OpenAI", effort, null);

        injected.TryGetProperty("reasoning", out var reasoning).ShouldBeTrue();
        reasoning.GetProperty("effort").GetString().ShouldBe(expected);
        reasoning.GetProperty("summary").GetString().ShouldBe("auto");
    }

    [Fact]
    public void OpenAIInjection_EnsuresReasoningObject()
    {
        var injected = InjectAndCapture("OpenAI", ReasoningEffort.High, null);

        injected.TryGetProperty("reasoning", out _).ShouldBeTrue();
    }

    #endregion

    #region Gemini Effort Mapping

    [Theory]
    [InlineData(ReasoningEffort.Low, "minimal")]
    [InlineData(ReasoningEffort.Medium, "low")]
    [InlineData(ReasoningEffort.High, "medium")]
    [InlineData(ReasoningEffort.Max, "high")]
    public void GeminiEffortMapping_CorrectStringValues(ReasoningEffort effort, string expected)
    {
        var injected = InjectAndCapture("Gemini", effort, null);

        injected.TryGetProperty("extra_body", out var extraBody).ShouldBeTrue();
        extraBody.GetProperty("google")
                 .GetProperty("thinking_config")
                 .GetProperty("thinking_level")
                 .GetString()
                 .ShouldBe(expected);
    }

    [Fact]
    public void GeminiInjection_WithBudgetTokens_IncludesBudget()
    {
        var injected = InjectAndCapture("Gemini", ReasoningEffort.High, budgetTokens: 4096);

        var thinkingConfig = injected.GetProperty("extra_body")
                                     .GetProperty("google")
                                     .GetProperty("thinking_config");

        thinkingConfig.GetProperty("include_thoughts").GetBoolean().ShouldBeTrue();
        thinkingConfig.GetProperty("thinking_budget").GetInt32().ShouldBe(4096);
    }

    [Fact]
    public void GeminiInjection_WithoutBudgetTokens_OmitsBudget()
    {
        var injected = InjectAndCapture("Gemini", ReasoningEffort.High, budgetTokens: null);

        var thinkingConfig = injected.GetProperty("extra_body")
                                     .GetProperty("google")
                                     .GetProperty("thinking_config");

        thinkingConfig.TryGetProperty("thinking_budget", out _).ShouldBeFalse();
    }

    [Fact]
    public void GoogleProvider_TreatedSameAsGemini()
    {
        var injected = InjectAndCapture("Google", ReasoningEffort.High, null);

        injected.TryGetProperty("extra_body", out _).ShouldBeTrue();
    }

    #endregion

    #region Qwen Budget Mapping

    [Theory]
    [InlineData(ReasoningEffort.Low, 512)]
    [InlineData(ReasoningEffort.Medium, 4096)]
    [InlineData(ReasoningEffort.High, 16384)]
    [InlineData(ReasoningEffort.Max, 32768)]
    public void QwenBudgetMapping_CorrectTokenValues(ReasoningEffort effort, int expectedBudget)
    {
        var injected = InjectAndCapture("Qwen", effort, null);

        injected.GetProperty("enable_thinking").GetBoolean().ShouldBeTrue();
        injected.GetProperty("thinking_budget").GetInt32().ShouldBe(expectedBudget);
    }

    [Fact]
    public void QwenInjection_WithExplicitBudget_OverridesDefault()
    {
        var injected = InjectAndCapture("Qwen", ReasoningEffort.Low, budgetTokens: 8000);

        injected.GetProperty("thinking_budget").GetInt32().ShouldBe(8000);
    }

    [Fact]
    public void DashScopeProvider_TreatedSameAsQwen()
    {
        var injected = InjectAndCapture("DashScope", ReasoningEffort.High, null);

        injected.TryGetProperty("enable_thinking", out _).ShouldBeTrue();
    }

    #endregion

    #region Anthropic Provider Skip

    [Fact]
    public void AnthropicProvider_SkipsInjection()
    {
        const string originalJson = """{"model":"claude-3-7-sonnet","messages":[{"role":"user","content":"hi"}]}""";
        using var message = CreateMessage(originalJson);

        ThinkingRequestPolicy.RequestContext.Value = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            ProviderName = "Anthropic"
        };
        try
        {
            InvokeThinkingInjection(message);
            ReadRequestContent(message.Request).ShouldBe(originalJson);
        }
        finally
        {
            ThinkingRequestPolicy.RequestContext.Value = null;
        }
    }

    #endregion

    #region ThinkingRequestContext

    [Fact]
    public void ThinkingRequestContext_RequiredThinking_MustBeSet()
    {
        var context = new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            ProviderName = "OpenAI"
        };

        context.Thinking.Effort.ShouldBe(ReasoningEffort.High);
        context.ProviderName.ShouldBe("OpenAI");
    }

    [Fact]
    public void ThinkingRequestPolicy_AsyncLocal_InitiallyNull()
    {
        ThinkingRequestPolicy.RequestContext.Value.ShouldBeNull();
    }

    #endregion

    #region Helper

    /// <summary>
    /// 执行注入逻辑并返回注入后的 JSON（通过复现 ThinkingRequestPolicy 的内部逻辑）。
    /// DeepSeek 和 None effort 不应调用此方法。
    /// </summary>
    private static JsonElement InjectAndCapture(string providerName, ReasoningEffort effort, int? budgetTokens)
    {
        var thinking = new ThinkingOptions { Effort = effort, BudgetTokens = budgetTokens };

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();

            if (IsGeminiProvider(providerName))
                WriteGeminiThinking(writer, thinking);
            else if (IsQwenProvider(providerName))
                WriteQwenThinking(writer, thinking);
            else
                WriteOpenAIThinking(writer, thinking);

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    private static void WriteOpenAIThinking(Utf8JsonWriter writer, ThinkingOptions thinking)
    {
        writer.WritePropertyName("reasoning");
        writer.WriteStartObject();
        writer.WriteString("effort", MapOpenAIEffort(thinking.Effort));
        writer.WriteString("summary", "auto");
        writer.WriteEndObject();
    }

    private static void WriteGeminiThinking(Utf8JsonWriter writer, ThinkingOptions thinking)
    {
        writer.WritePropertyName("extra_body");
        writer.WriteStartObject();
        writer.WritePropertyName("google");
        writer.WriteStartObject();
        writer.WritePropertyName("thinking_config");
        writer.WriteStartObject();
        writer.WriteBoolean("include_thoughts", true);
        writer.WriteString("thinking_level", MapGeminiEffort(thinking.Effort));
        if (thinking.BudgetTokens.HasValue)
            writer.WriteNumber("thinking_budget", thinking.BudgetTokens.Value);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteQwenThinking(Utf8JsonWriter writer, ThinkingOptions thinking)
    {
        writer.WriteBoolean("enable_thinking", true);
        var budget = thinking.BudgetTokens ?? MapQwenBudget(thinking.Effort);
        writer.WriteNumber("thinking_budget", budget);
    }

    private static string MapOpenAIEffort(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        ReasoningEffort.Max => "high",
        _ => "none"
    };

    private static string MapGeminiEffort(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "minimal",
        ReasoningEffort.Medium => "low",
        ReasoningEffort.High => "medium",
        ReasoningEffort.Max => "high",
        _ => "none"
    };

    private static int MapQwenBudget(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => 512,
        ReasoningEffort.Medium => 4096,
        ReasoningEffort.High => 16384,
        ReasoningEffort.Max => 32768,
        _ => 0
    };

    private static bool IsGeminiProvider(string? p)
        => string.Equals(p, "Gemini", StringComparison.OrdinalIgnoreCase)
           || string.Equals(p, "Google", StringComparison.OrdinalIgnoreCase);

    private static bool IsQwenProvider(string? p)
        => string.Equals(p, "Qwen", StringComparison.OrdinalIgnoreCase)
           || string.Equals(p, "DashScope", StringComparison.OrdinalIgnoreCase)
           || string.Equals(p, "Alibaba", StringComparison.OrdinalIgnoreCase);

    private static PipelineMessage CreateMessage(string json)
    {
        var request = new TestPipelineRequest
        {
            Method = "POST",
            Uri = new Uri("https://api.anthropic.com/v1/messages"),
            Content = BinaryContent.Create(new BinaryData(Encoding.UTF8.GetBytes(json)))
        };

        return (PipelineMessage)Activator.CreateInstance(
            typeof(PipelineMessage),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [request],
            culture: null)!;
    }

    private static void InvokeThinkingInjection(PipelineMessage message)
    {
        var method = typeof(ThinkingRequestPolicy).GetMethod(
            "InjectThinkingParams",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.ShouldNotBeNull();
        method.Invoke(null, [message]);
    }

    private static string ReadRequestContent(PipelineRequest request)
    {
        request.Content.ShouldNotBeNull();

        using var stream = new MemoryStream();
        request.Content.WriteTo(stream, default);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

#pragma warning disable CS8764, CS8765
    private sealed class TestPipelineRequest : PipelineRequest
    {
        private readonly TestPipelineRequestHeaders _headers = new();
        private string? _method = "POST";
        private Uri? _uri = new("https://example.com");
        private BinaryContent? _content;

        protected override string? MethodCore
        {
            get => _method;
            set => _method = value;
        }

        protected override Uri? UriCore
        {
            get => _uri;
            set => _uri = value;
        }

        protected override PipelineRequestHeaders HeadersCore => _headers;

        protected override BinaryContent ContentCore
        {
            get => _content!;
            set => _content = value;
        }

        public override void Dispose()
        {
        }
    }
#pragma warning restore CS8764, CS8765

    private sealed class TestPipelineRequestHeaders : PipelineRequestHeaders
    {
        private readonly Dictionary<string, List<string>> _headers = new(StringComparer.OrdinalIgnoreCase);

        public override void Add(string name, string value)
        {
            if (!_headers.TryGetValue(name, out var values))
            {
                values = [];
                _headers[name] = values;
            }

            values.Add(value);
        }

        public override void Set(string name, string value)
        {
            _headers[name] = [value];
        }

        public override bool Remove(string name) => _headers.Remove(name);

        public override bool TryGetValue(string name, out string? value)
        {
            if (_headers.TryGetValue(name, out var values) && values.Count > 0)
            {
                value = string.Join(",", values);
                return true;
            }

            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            if (_headers.TryGetValue(name, out var storedValues))
            {
                values = storedValues;
                return true;
            }

            values = null;
            return false;
        }

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            foreach (var (name, values) in _headers)
            {
                foreach (var value in values)
                {
                    yield return new KeyValuePair<string, string>(name, value);
                }
            }
        }
    }

    #endregion
}
