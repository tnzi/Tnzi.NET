using System.Text;

namespace Tnzi.AI.Tests.Guardrails;

/// <summary>
/// <c>GuardrailResult.Modified</c>（脱敏等）在流式输出与并行模式下都必须真的生效。
/// </summary>
/// <remarks>
/// 流式输出此前丢弃改写结果、原样放行缓冲的 chunk：非流式响应被掩码，SSE（界面默认路径）却把原文流给客户端。
/// 并行模式此前对每个结果取 <c>ModifiedContent</c> 逐个覆盖，而每个改写都是对原文算的：
/// 两个改写守卫时前一个的脱敏被后一个基于原文的输出整个盖掉。
/// </remarks>
public class GuardrailModificationTests
{
    private const string Card = "4111-1111-1111-1111";
    private const string Masked = "[CARD]";

    #region 流式输出

    [Fact]
    public async Task Streaming_ModifyingGuardrail_ClientReceivesTheModifiedText()
    {
        var middleware = CreateOutputMiddleware([new MaskGuardrail(Card, Masked)], bufferSize: 40, overlapSize: 5);

        var text = await StreamAsync(middleware, "Your card ", Card, " was charged twice ", "and refunded today. ", "Thanks for waiting.");

        text.ShouldNotContain(Card);
        text.ShouldBe($"Your card {Masked} was charged twice and refunded today. Thanks for waiting.");
    }

    [Fact]
    public async Task Streaming_ShortResponseCheckedAtEndOfStream_ClientReceivesTheModifiedText()
    {
        var middleware = CreateOutputMiddleware([new MaskGuardrail(Card, Masked)], bufferSize: 500, overlapSize: 50);

        var text = await StreamAsync(middleware, "card ", Card);

        text.ShouldBe($"card {Masked}");
    }

    [Fact]
    public async Task Streaming_ModificationReachingAlreadySentText_BlocksInsteadOfLeaking()
    {
        // 卡号跨在窗口边界上：前半截已随上一个窗口发出（收不回来，它还留在重叠区里），
        // 下一个窗口凑齐了整个卡号、改写要动到已发出的那半截：后半截不能再按原样放行
        var middleware = CreateOutputMiddleware([new MaskGuardrail(Card, Masked)], bufferSize: 20, overlapSize: 15);

        var chunks = await CollectAsync(middleware, "0123456789", "4111-1111-", "1111-1111", " done");

        chunks.Last().FinishReason.ShouldBe(FinishReasons.GuardrailRejected);
        string.Concat(chunks.Select(c => c.Text)).ShouldNotContain(Card);
    }

    /// <summary>
    /// 下一个窗口的重叠起点落在 emoji 的后半个上时往后挪一位：窗口原样交给每个输出守卫，以孤立的低代理项开头的话，
    /// 按 Unicode 处理文本的守卫（这里是 NFKC 归一化）直接抛异常，整段流式输出就断了。
    /// </summary>
    [Fact]
    public async Task Streaming_OverlapStartingInsideASurrogatePair_HandsNoLoneSurrogateToTheGuardrails()
    {
        var recorder = new NormalizingRecorderGuardrail();
        // 窗口 6、重叠 3：第一个窗口 "ab" + emoji + "cd" 共 6 个码元，重叠起点（6 - 3 = 3）是 emoji 的后半个
        var middleware = CreateOutputMiddleware([recorder], bufferSize: 6, overlapSize: 3);

        var text = await StreamAsync(middleware, "ab\U0001F600cd", "efgh", "ij");

        text.ShouldBe("ab\U0001F600cdefghij");
        recorder.Windows.Count.ShouldBeGreaterThan(1);
        recorder.Windows.ShouldAllBe(w => !char.IsLowSurrogate(w[0]));
        recorder.Windows[1].ShouldBe("cdefgh");
    }

    [Fact]
    public async Task Streaming_ModifiedWindow_KeepsNonTextChunkFieldsAndDoesNotMutateTheSourceChunks()
    {
        var middleware = CreateOutputMiddleware([new MaskGuardrail(Card, Masked)], bufferSize: 500, overlapSize: 50);
        var usage = new TokenUsageDto { InputTokens = 7, OutputTokens = 3 };
        var source = new List<AgentStreamChunk>
        {
            new() { Text = "card " },
            new() { Text = Card, Usage = usage, FinishReason = "stop" }
        };

        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(CreateContext(), (_, _) => source.ToAsyncEnumerable()))
        {
            chunks.Add(chunk);
        }

        string.Concat(chunks.Select(c => c.Text)).ShouldBe($"card {Masked}");
        chunks.ShouldContain(c => c.Usage == usage && c.FinishReason == "stop");
        source[1].Text.ShouldBe(Card);
    }

    #endregion

    #region 并行模式

    [Fact]
    public async Task ParallelInput_TwoModifyingGuardrails_BothModificationsApply()
    {
        var runner = CreateRunner(
            input: [new MaskGuardrail(Card, Masked), new UpperCaseGuardrail()],
            output: []);

        var (text, rejection) = await runner.RunInputGuardrailsAsync($"my card is {Card}");

        rejection.ShouldBeNull();
        text.ShouldBe($"MY CARD IS {Masked}");
    }

    [Fact]
    public async Task ParallelOutput_TwoModifyingGuardrails_BothModificationsApply()
    {
        var runner = CreateRunner(
            input: [],
            output: [new MaskGuardrail(Card, Masked), new UpperCaseGuardrail()]);

        var (text, rejection) = await runner.RunOutputGuardrailsAsync($"your card is {Card}");

        rejection.ShouldBeNull();
        text.ShouldBe($"YOUR CARD IS {Masked}");
    }

    [Fact]
    public async Task ParallelInput_ReRunOnModifiedTextRejects_FailsClosed()
    {
        var runner = CreateRunner(
            input: [new MaskGuardrail(Card, Masked), new RejectOnMarkerGuardrail(Masked)],
            output: []);

        var (_, rejection) = await runner.RunInputGuardrailsAsync($"my card is {Card}");

        rejection.ShouldNotBeNull();
        rejection.GuardrailName.ShouldBe("marker");
    }

    #endregion

    #region Helpers

    private static GuardrailRunner CreateRunner(IEnumerable<IInputGuardrail> input, IEnumerable<IOutputGuardrail> output)
    {
        var options = new AIOptions();
        options.Guardrails.Enabled = true;
        options.Guardrails.ExecutionMode = GuardrailExecutionMode.Parallel;
        return new GuardrailRunner(input, output, new StaticOptionsMonitor<AIOptions>(options), NullLogger<GuardrailRunner>.Instance);
    }

    private static OutputGuardrailMiddleware CreateOutputMiddleware(IReadOnlyList<IOutputGuardrail> guardrails, int bufferSize, int overlapSize)
    {
        var options = new AIOptions();
        options.Guardrails.Enabled = true;
        options.Guardrails.StreamingBufferSize = bufferSize;
        options.Guardrails.StreamingOverlapSize = overlapSize;
        var monitor = new StaticOptionsMonitor<AIOptions>(options);
        var runner = new GuardrailRunner([], guardrails, monitor, NullLogger<GuardrailRunner>.Instance);
        return new OutputGuardrailMiddleware(runner, guardrails, monitor, NullLogger<OutputGuardrailMiddleware>.Instance);
    }

    private static AiMiddlewareContext CreateContext()
        => new()
        {
            Request = new AgentRunRequest { UserMessage = "hi", UserId = Guid.NewGuid() },
            Agent = AgentResolution.Success(agent: null!, provider: "test", model: "test-model", agentId: null),
            ServiceProvider = new Mock<IServiceProvider>().Object
        };

    private static async Task<string> StreamAsync(OutputGuardrailMiddleware middleware, params string[] parts)
        => string.Concat((await CollectAsync(middleware, parts)).Select(c => c.Text));

    private static async Task<List<AgentStreamChunk>> CollectAsync(OutputGuardrailMiddleware middleware, params string[] parts)
    {
        var chunks = new List<AgentStreamChunk>();
        await foreach (var chunk in middleware.InvokeStreamingAsync(
            CreateContext(),
            (_, _) => parts.Select(p => new AgentStreamChunk { Text = p }).ToAsyncEnumerable()))
        {
            chunks.Add(chunk);
        }
        return chunks;
    }

    private sealed class MaskGuardrail(string find, string replacement) : IInputGuardrail, IOutputGuardrail
    {
        public string Name => "mask";

        public Task<GuardrailResult> ValidateAsync(string text, CancellationToken ct = default)
            => Task.FromResult(text.Contains(find, StringComparison.Ordinal)
                ? GuardrailResult.Modified(Name, text.Replace(find, replacement, StringComparison.Ordinal), "masked")
                : GuardrailResult.Allowed());
    }

    /// <summary>记下每个收到的窗口，并像按 Unicode 处理文本的守卫那样先做 NFKC 归一化（孤立代理项会让它抛异常）。</summary>
    private sealed class NormalizingRecorderGuardrail : IOutputGuardrail
    {
        public string Name => "normalizing-recorder";

        public List<string> Windows { get; } = [];

        public Task<GuardrailResult> ValidateAsync(string text, CancellationToken ct = default)
        {
            Windows.Add(text);
            _ = text.Normalize(NormalizationForm.FormKC);
            return Task.FromResult(GuardrailResult.Allowed());
        }
    }

    private sealed class UpperCaseGuardrail : IInputGuardrail, IOutputGuardrail
    {
        public string Name => "upper";

        public Task<GuardrailResult> ValidateAsync(string text, CancellationToken ct = default)
        {
            var upper = text.ToUpperInvariant();
            return Task.FromResult(upper == text ? GuardrailResult.Allowed() : GuardrailResult.Modified(Name, upper, "normalized"));
        }
    }

    private sealed class RejectOnMarkerGuardrail(string marker) : IInputGuardrail
    {
        public string Name => "marker";

        public Task<GuardrailResult> ValidateAsync(string input, CancellationToken ct = default)
            => Task.FromResult(input.Contains(marker, StringComparison.Ordinal)
                ? GuardrailResult.Rejected(Name, "marker found")
                : GuardrailResult.Modified(Name, input + " ", "padded"));
    }

    #endregion
}
