using System.ClientModel;
using System.ClientModel.Primitives;
using System.Reflection;
using System.Text;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// ThinkingRequestPolicy 推理参数注入：对真实的注入逻辑断言出站请求体。
/// </summary>
/// <remarks>
/// 此前这里复刻了一份私有注入逻辑再对复刻品断言，于是 OpenAI 写成 Responses API 的 <c>reasoning</c> 对象
/// （Chat Completions 的参数是顶层字符串 <c>reasoning_effort</c>）这类错误被测试原样锁住。
/// </remarks>
public class ThinkingRequestPolicyTests : IDisposable
{
    public ThinkingRequestPolicyTests()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    public void Dispose()
    {
        ThinkingRequestPolicy.RequestContext.Value = null;
    }

    #region OpenAI

    [Theory]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.Max, "high")]
    public void OpenAIFormat_WritesTopLevelReasoningEffort(ReasoningEffort effort, string expected)
    {
        var body = Inject(ThinkingPayloadFormat.OpenAI, effort, model: "o4-mini");

        body.GetProperty("reasoning_effort").GetString().ShouldBe(expected);
        body.TryGetProperty("reasoning", out _).ShouldBeFalse();
    }

    [Fact]
    public void OpenAIFormat_ReplacesAReasoningEffortTheSdkAlreadyWrote()
    {
        var body = Inject(ThinkingPayloadFormat.OpenAI, ReasoningEffort.Low, model: "o4-mini", extraJson: "\"reasoning_effort\":\"high\",");

        body.GetProperty("reasoning_effort").GetString().ShouldBe("low");
    }

    #endregion

    #region Gemini

    [Theory]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.Max, "high")]
    public void GeminiFormat_Gemini3_WritesThinkingLevelOnly(ReasoningEffort effort, string expected)
    {
        var config = GeminiThinkingConfig(Inject(ThinkingPayloadFormat.Gemini, effort, model: "gemini-3-pro-preview", budgetTokens: 4096));

        config.GetProperty("include_thoughts").GetBoolean().ShouldBeTrue();
        config.GetProperty("thinking_level").GetString().ShouldBe(expected);
        config.TryGetProperty("thinking_budget", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(ReasoningEffort.Low, 1024)]
    [InlineData(ReasoningEffort.Medium, 8192)]
    [InlineData(ReasoningEffort.High, 24576)]
    public void GeminiFormat_Gemini25_WritesThinkingBudgetOnly(ReasoningEffort effort, int expected)
    {
        var config = GeminiThinkingConfig(Inject(ThinkingPayloadFormat.Gemini, effort, model: "gemini-2.5-flash"));

        config.GetProperty("thinking_budget").GetInt32().ShouldBe(expected);
        config.TryGetProperty("thinking_level", out _).ShouldBeFalse();
    }

    [Fact]
    public void GeminiFormat_Gemini25_ExplicitBudgetWins()
    {
        var config = GeminiThinkingConfig(Inject(ThinkingPayloadFormat.Gemini, ReasoningEffort.High, model: "gemini-2.5-pro", budgetTokens: 4096));

        config.GetProperty("thinking_budget").GetInt32().ShouldBe(4096);
    }

    #endregion

    #region Qwen

    [Theory]
    [InlineData(ReasoningEffort.Low, 512)]
    [InlineData(ReasoningEffort.Medium, 4096)]
    [InlineData(ReasoningEffort.High, 16384)]
    [InlineData(ReasoningEffort.Max, 32768)]
    public void QwenFormat_WritesEnableThinkingAndBudget(ReasoningEffort effort, int expectedBudget)
    {
        var body = Inject(ThinkingPayloadFormat.Qwen, effort, model: "qwen3-max");

        body.GetProperty("enable_thinking").GetBoolean().ShouldBeTrue();
        body.GetProperty("thinking_budget").GetInt32().ShouldBe(expectedBudget);
    }

    [Fact]
    public void QwenFormat_ExplicitBudgetOverridesTheEffortDefault()
    {
        var body = Inject(ThinkingPayloadFormat.Qwen, ReasoningEffort.Low, model: "qwen3-max", budgetTokens: 8000);

        body.GetProperty("thinking_budget").GetInt32().ShouldBe(8000);
    }

    #endregion

    #region 不注入的形态

    [Theory]
    [InlineData(ThinkingPayloadFormat.None)]
    [InlineData(ThinkingPayloadFormat.Anthropic)]
    public void FormatsWithoutBodyInjection_LeaveTheBodyUntouched(ThinkingPayloadFormat format)
    {
        const string originalJson = """{"model":"kimi-k2","messages":[{"role":"user","content":"hi"}]}""";

        RunPolicy(originalJson, new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.High },
            Format = format
        }).ShouldBe(originalJson);
    }

    [Fact]
    public void EffortNone_LeavesTheBodyUntouched()
    {
        const string originalJson = """{"model":"o4-mini","messages":[{"role":"user","content":"hi"}]}""";

        RunPolicy(originalJson, new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.None },
            Format = ThinkingPayloadFormat.OpenAI
        }).ShouldBe(originalJson);
    }

    [Fact]
    public void DeepSeekFormat_BackfillsReasoningContentEvenWithThinkingOff()
    {
        const string json = """{"model":"deepseek-reasoner","messages":[{"role":"user","content":"hi"},{"role":"assistant","content":"ok"}]}""";

        var body = JsonDocument.Parse(RunPolicy(json, new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.None },
            Format = ThinkingPayloadFormat.DeepSeek
        })).RootElement;

        body.GetProperty("messages")[1].GetProperty("reasoning_content").GetString().ShouldBe(string.Empty);
    }

    [Fact]
    public void ContextWithoutFormat_FallsBackToTheEntryName()
    {
        // 直接设置上下文、不经过装饰器的调用方（只给了名字）仍按名字路由
        const string json = """{"model":"gemini-2.5-flash","messages":[]}""";

        var body = JsonDocument.Parse(RunPolicy(json, new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = ReasoningEffort.Medium },
            ProviderName = "Google"
        })).RootElement;

        body.TryGetProperty("extra_body", out _).ShouldBeTrue();
    }

    #endregion

    #region 格式识别

    [Theory]
    [InlineData("OpenAI", "OpenAI", null, "o4-mini", ThinkingPayloadFormat.OpenAI)]
    [InlineData("gemini-prod", "Gemini", null, "gemini-2.5-pro", ThinkingPayloadFormat.Gemini)]
    [InlineData("proxy", "OpenAI", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3-pro-preview", ThinkingPayloadFormat.Gemini)]
    [InlineData("tongyi", "OpenAI", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen3-max", ThinkingPayloadFormat.Qwen)]
    [InlineData("ds", "OpenAI", "https://api.deepseek.com", "deepseek-reasoner", ThinkingPayloadFormat.DeepSeek)]
    [InlineData("Kimi", "OpenAI", "https://api.moonshot.cn/v1", "kimi-k2", ThinkingPayloadFormat.None)]
    [InlineData("zhipu", "OpenAI", "https://open.bigmodel.cn/api/paas/v4", "glm-4.6", ThinkingPayloadFormat.None)]
    [InlineData("mm", "OpenAI", null, "MiniMax-M2", ThinkingPayloadFormat.None)]
    [InlineData("Anthropic", "Anthropic", null, "claude-sonnet-4", ThinkingPayloadFormat.Anthropic)]
    public void Resolve_UsesTypeNameHostAndModel(string name, string type, string? baseUrl, string model, ThinkingPayloadFormat expected)
    {
        var options = new ProviderOptions { Name = name, ProviderType = type, BaseUrl = baseUrl };

        ThinkingPayloadFormats.Resolve(options, model).ShouldBe(expected);
    }

    #endregion

    #region Helper

    private static JsonElement GeminiThinkingConfig(JsonElement body)
        => body.GetProperty("extra_body").GetProperty("google").GetProperty("thinking_config");

    private static JsonElement Inject(ThinkingPayloadFormat format, ReasoningEffort effort, string model, int? budgetTokens = null, string extraJson = "")
    {
        var json = $$"""{{{extraJson}}"model":"{{model}}","messages":[{"role":"user","content":"hi"}]}""";
        var result = RunPolicy(json, new ThinkingRequestContext
        {
            Thinking = new ThinkingOptions { Effort = effort, BudgetTokens = budgetTokens },
            Format = format
        });
        return JsonDocument.Parse(result).RootElement.Clone();
    }

    private static string RunPolicy(string json, ThinkingRequestContext context)
    {
        using var message = CreateMessage(json);
        ThinkingRequestPolicy.RequestContext.Value = context;
        try
        {
            ThinkingRequestPolicy.InjectThinkingParams(message);
            return ReadRequestContent(message.Request);
        }
        finally
        {
            ThinkingRequestPolicy.RequestContext.Value = null;
        }
    }

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
