namespace Tnzi.AI.Tests.Architecture;

/// <summary>
/// 防复活门禁：曾经存在、文档写成扩展点、生产路径却零调用方的类型不得再回来。
/// </summary>
/// <remarks>
/// 2026-09-12 删除的 <c>IConversationStore</c> / <c>DatabaseConversationStore</c> /
/// <c>HistoryStoreOptions.Enabled</c>：历史由 <c>HistoryMiddleware</c> 经
/// <c>IAgentThreadInternalService</c> 持久化，那套「可替换的对话存储」注册成功、启动无告警、
/// 一次都不会被调用，`AI:History:Store:Enabled=false` 也照样每轮落库。
/// 要恢复它，必须让 HistoryMiddleware 真的经由它读写，并让本测试随之退役。
/// </remarks>
public class DeadExtensionPointTests
{
    [Theory]
    [InlineData("Tnzi.AI.Services.IConversationStore")]
    [InlineData("Tnzi.AI.Infrastructure.Stores.DatabaseConversationStore")]
    [InlineData("Tnzi.AI.Dtos.ConversationSummary")]
    public void RemovedConversationStoreTypes_DoNotComeBack(string fullName)
    {
        typeof(AIModule).Assembly.GetType(fullName).ShouldBeNull(
            $"{fullName} was removed because nothing in the runtime called it; wire it into HistoryMiddleware before restoring it");
    }

    [Fact]
    public void HistoryStoreOptions_HasNoDeadEnabledSwitch()
    {
        typeof(HistoryStoreOptions).GetProperty("Enabled").ShouldBeNull(
            "AI:History:Store:Enabled had no runtime reader; history persistence is not switchable");
    }
}
