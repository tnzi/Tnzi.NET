namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// <c>ContextProviders:MaxTokenBudget</c> 只约束注入的消息，不约束工具。
/// </summary>
/// <remarks>
/// 工具不占消息 token。此前预算一满组合器就整轮 break，后面的 provider 一个都不调用；某个 provider 的消息放不下时
/// 连它的工具一起丢。Skills 在 Instructions / Both 模式下同时返回摘要消息、skill_* 工具与已激活技能的函数，
/// 摘要放不下的那一轮模型三者都拿不到，只留一条 Debug 日志。
/// </remarks>
public class CompositeContextBudgetTests
{
    [Fact]
    public async Task ProviderWhoseMessagesDoNotFit_StillContributesItsTools_ButNotItsMessagesOrCitations()
    {
        var composite = Create(budget: 100,
            new StubProvider("memory", order: 0, messageLength: 90),
            new StubProvider("skills", order: 1, messageLength: 50, tool: "skill_activate", citation: "skills-doc"));

        var injection = await composite.GetContextAsync([new ChatMessage(ChatRole.User, "hi")]);

        injection.Messages.ShouldNotBeNull();
        injection.Messages!.Select(m => m.Text).ShouldBe([new string('m', 90)]);
        injection.Tools.ShouldNotBeNull();
        injection.Tools!.Select(t => t.Name).ShouldBe(["skill_activate"]);
        injection.Citations.ShouldBeNull();
    }

    [Fact]
    public async Task BudgetFilledExactly_ToolOnlyProvidersAfterItAreStillCalled()
    {
        var toolOnly = new StubProvider("on-demand", order: 1, messageLength: 0, tool: "search_history");
        var composite = Create(budget: 100,
            new StubProvider("rag", order: 0, messageLength: 100),
            toolOnly);

        var injection = await composite.GetContextAsync([new ChatMessage(ChatRole.User, "hi")]);

        toolOnly.Calls.ShouldBe(1);
        injection.Tools.ShouldNotBeNull();
        injection.Tools!.Select(t => t.Name).ShouldBe(["search_history"]);
    }

    [Fact]
    public async Task MessagesThatFit_AreKeptAlongWithTheirCitations()
    {
        var composite = Create(budget: 100,
            new StubProvider("rag", order: 0, messageLength: 40, citation: "doc-a"),
            new StubProvider("history", order: 1, messageLength: 40, citation: "doc-b"));

        var injection = await composite.GetContextAsync([new ChatMessage(ChatRole.User, "hi")]);

        injection.Messages!.Count.ShouldBe(2);
        injection.Citations!.Select(c => c.SourceName).ShouldBe(["doc-a", "doc-b"]);
    }

    private static CompositeContextProvider Create(int budget, params IContextProvider[] providers)
    {
        var options = new AIOptions();
        options.ContextProviders.MaxTokenBudget = budget;
        return new CompositeContextProvider(
            providers,
            NullLogger<CompositeContextProvider>.Instance,
            new StaticOptionsMonitor<AIOptions>(options),
            new LengthTokenEstimator());
    }

    /// <summary>一个字符算一个 token，让预算边界在用例里一眼可算。</summary>
    private sealed class LengthTokenEstimator : ITokenEstimator
    {
        public int Estimate(string text, int baseOverhead = 500) => text.Length + baseOverhead;
    }

    private sealed class StubProvider(string name, int order, int messageLength, string? tool = null, string? citation = null) : IContextProvider
    {
        public int Calls { get; private set; }

        public string Name => name;

        public int Order => order;

        public Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ContextInjection
            {
                Messages = messageLength > 0 ? [new ChatMessage(ChatRole.System, new string(name[0], messageLength))] : null,
                Tools = tool != null ? [AIFunctionFactory.Create(() => "ok", tool)] : null,
                Citations = citation != null ? [new CitationDto { SourceName = citation, Text = "excerpt" }] : null
            });
        }
    }
}
