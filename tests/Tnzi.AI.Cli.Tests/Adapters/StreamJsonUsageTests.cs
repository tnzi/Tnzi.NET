namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// stream-json 适配器交给执行器的用量：result 帧的会话累计值与本轮主循环用量分开报，没有 result 帧时用逐帧看到的。
/// </summary>
public class StreamJsonUsageTests
{
    private static CliAgentLaunchContext Context() => new()
    {
        Provider = CliBuiltInProviders.All["claude"],
        ExecutablePath = "/nonexistent/claude",
        Prompt = "hello",
        WorkingDirectory = "/tmp/does-not-need-to-exist"
    };

    private static async Task<CliAgentResult> RunAsync(string[] frames, CliSessionOutcome? outcome = null)
    {
        var adapter = new StreamJsonAdapter(NullLogger<StreamJsonAdapter>.Instance);
        await foreach (var _ in adapter.RunAsync(new FakeCliAgentTransport(frames), Context(), CancellationToken.None))
        {
        }

        return adapter.GetResult(outcome ?? new CliSessionOutcome { ExitCode = 0 });
    }

    [Fact]
    public async Task ResultFrame_ReportsModelUsageAsTheSessionTotal_AndTheTurnsMainLoopUsageSeparately()
    {
        var result = await RunAsync(
        [
            """{"type":"assistant","message":{"id":"msg_1","role":"assistant","model":"claude-sonnet-5-5","usage":{"input_tokens":2,"output_tokens":751,"cache_read_input_tokens":67369,"cache_creation_input_tokens":996},"content":[{"type":"text","text":"hi"}]}}""",
            """{"type":"result","subtype":"success","result":"hi","usage":{"input_tokens":2,"output_tokens":751,"cache_read_input_tokens":67369,"cache_creation_input_tokens":996},"modelUsage":{"claude-sonnet-5-5":{"inputTokens":42,"outputTokens":15140,"cacheReadInputTokens":859097,"cacheCreationInputTokens":58628,"costUSD":0.557815},"claude-haiku-4-5":{"inputTokens":115978,"outputTokens":4848,"cacheReadInputTokens":0,"cacheCreationInputTokens":0,"costUSD":0.240218}}}"""
        ]);

        result.UsageIsSessionTotal.ShouldBeTrue();
        result.Usage["claude-sonnet-5-5"].OutputTokens.ShouldBe(15140);
        result.Usage["claude-sonnet-5-5"].ReportedCostUsd.ShouldBe(0.557815m);
        result.Usage["claude-haiku-4-5"].InputTokens.ShouldBe(115978);
        result.TurnUsage.ShouldBe(new CliAgentTokenUsage { InputTokens = 2, OutputTokens = 751, CacheReadTokens = 67369, CacheWriteTokens = 996 });
    }

    [Fact]
    public async Task NoResultFrame_UsesTheStreamedUsage_CountingEachApiReplyOnce()
    {
        // claude 把一次回复的每个内容块拆成一帧，几帧带着同一个 id 与同一份用量：按帧相加会把一次调用记成好几次
        var result = await RunAsync(
            [
                """{"type":"assistant","message":{"id":"msg_1","role":"assistant","model":"m","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":100},"content":[{"type":"thinking","thinking":"..."}]}}""",
                """{"type":"assistant","message":{"id":"msg_1","role":"assistant","model":"m","usage":{"input_tokens":10,"output_tokens":5,"cache_read_input_tokens":100},"content":[{"type":"tool_use","id":"t1","name":"Read","input":{}}]}}""",
                """{"type":"assistant","message":{"id":"msg_2","role":"assistant","model":"m","usage":{"input_tokens":3,"output_tokens":2,"cache_read_input_tokens":110},"content":[{"type":"text","text":"partial"}]}}"""
            ],
            new CliSessionOutcome { ExitCode = null, Cancelled = true });

        result.UsageIsSessionTotal.ShouldBeFalse();
        result.TurnUsage.ShouldBeNull();
        result.Usage["m"].ShouldBe(new CliAgentTokenUsage { InputTokens = 13, OutputTokens = 7, CacheReadTokens = 210 });
    }

    [Fact]
    public async Task FramesWithoutAnId_AreEachCounted()
    {
        var result = await RunAsync(
            [
                """{"type":"assistant","message":{"role":"assistant","model":"m","usage":{"input_tokens":1,"output_tokens":1},"content":[{"type":"text","text":"a"}]}}""",
                """{"type":"assistant","message":{"role":"assistant","model":"m","usage":{"input_tokens":1,"output_tokens":1},"content":[{"type":"text","text":"b"}]}}"""
            ],
            new CliSessionOutcome { Cancelled = true });

        result.Usage["m"].InputTokens.ShouldBe(2);
    }

    [Fact]
    public async Task ZeroedTotalsOnAStartupError_FallBackToWhatWasStreamed()
    {
        // 启动出错的 result 可能带一份全零的累计值，那时逐帧看到的才是已经花掉的那部分
        var result = await RunAsync(
        [
            """{"type":"assistant","message":{"id":"msg_1","role":"assistant","model":"m","usage":{"input_tokens":7,"output_tokens":3},"content":[{"type":"text","text":"x"}]}}""",
            """{"type":"result","subtype":"error_during_execution","is_error":true,"result":"boom","modelUsage":{"m":{"inputTokens":0,"outputTokens":0,"cacheReadInputTokens":0,"cacheCreationInputTokens":0,"costUSD":0}}}"""
        ]);

        result.UsageIsSessionTotal.ShouldBeFalse();
        result.Usage["m"].InputTokens.ShouldBe(7);
    }
}
