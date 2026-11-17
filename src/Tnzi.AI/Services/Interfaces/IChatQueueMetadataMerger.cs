namespace Tnzi.AI.Services;

/// <summary>
/// 线程的几条排队消息合成一轮发出时，它们各自的每轮元数据怎么合成这一轮的那一份。
/// </summary>
/// <remarks>
/// <para>
/// 每轮元数据是应用自己定义的值（框架不解释它），其中有些键不能按「后写的为准」合并。典型的是<b>权限一类的键</b>：
/// 用户排了一条「先出计划」、又排了一条「全权」，按先后覆盖合成的这一轮就以全权运行，第一条消息上用户自己选的限制被悄悄绕过了。
/// 这类键要取几条里最严的那个值，而「哪个更严」只有应用知道，所以由应用决定。
/// </para>
/// <para>
/// 默认实现 <see cref="ConfiguredChatQueueMetadataMerger"/>：<c>AI:Chat:Queue:StrictestValues</c> 里列出的键取最严的值
/// （按配置给的顺序，越靠前越严），其余的键按写下的先后合并、后写的为准。比较规则写不进配置的应用注册自己的实现即可替换它
/// （默认实现以 <c>TryAdd</c> 在 PostConfigure 阶段注册），它是 Scoped 服务。
/// </para>
/// <para>
/// 合成的结果照常经过聊天服务的元数据校验（键数、大小）与 <see cref="ICliAgentTurnHandler"/>，与用户自己发出的一轮一样。
/// 只有一条消息时也会调用，应用可以借此统一处理。
/// </para>
/// </remarks>
public interface IChatQueueMetadataMerger
{
    /// <summary>这一轮的元数据；null 或空 = 不带元数据。</summary>
    /// <param name="context">要合成的几条消息的元数据（按写下的先后）与它们所在的线程、用户、Agent。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyDictionary<string, JsonElement>?> MergeAsync(ChatQueueMergeContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// 交给 <see cref="IChatQueueMetadataMerger"/> 的一次合成。
/// </summary>
/// <param name="ThreadId">线程。</param>
/// <param name="UserId">线程主人（这一轮以这个用户的身份发出）。</param>
/// <param name="AgentId">线程的 Agent；不绑 Agent 时为 null。</param>
/// <param name="Metadata">每条消息的元数据，按写下的先后；没有元数据的消息是一个空字典（不会被跳过，条数与消息数相同）。</param>
public sealed record ChatQueueMergeContext(
    Guid ThreadId,
    Guid UserId,
    Guid? AgentId,
    IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Metadata);
