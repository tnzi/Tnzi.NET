namespace Tnzi.AI.Metadata;

/// <summary>
/// 线程在不在主人的对话列表里（线程列表、「最近对话」及其搜索、在跑 / 未读计数）。
/// </summary>
/// <remarks>
/// <para>
/// 只有服务端代码能写：建线程时经 <c>CreateAgentThreadDto.Listing</c>、<c>AgentRunRequest.ThreadListing</c>、
/// <c>AiClientOptions.ThreadListing</c> 或直接写实体，之后经 <c>IAgentThreadInternalService.SetListingAsync</c>。
/// 没有任何请求体能带它（与 <c>Origin</c> 同一纪律）。
/// </para>
/// <para>
/// 不在列表里的线程照常按 id 可用：读它和它的消息、跟随正在跑的一轮、标记已读、在里面发消息、删除。
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ThreadListing>))]
public enum ThreadListing
{
    /// <summary>在列表里（默认，也是本列出现之前就有的线程）。</summary>
    Listed = 0,

    /// <summary>
    /// 先不进列表；主人自己在里面说了话（经聊天端点的一轮，或往它的队列里放了一条消息）就变成 <see cref="Listed"/>，
    /// 从此与别的线程一样。服务端代码替主人跑的一轮（<c>IAgentDispatchFacade</c> / <c>IAgentRuntime</c> /
    /// <c>ITnziAiClient</c>）不算。适合无人值守的运行（定时任务、Webhook、收信触发）建的线程。
    /// </summary>
    UntilUserMessage = 1,

    /// <summary>
    /// 永远不进列表，主人在里面说话也不改变它；只有服务端代码能再改。适合应用在自己页面上展示的只读记录。
    /// </summary>
    Never = 2,
}
