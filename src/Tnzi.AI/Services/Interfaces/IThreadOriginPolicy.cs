namespace Tnzi.AI.Services;

/// <summary>
/// 决定聊天端点新建的线程带什么来源（<see cref="AgentThread.Origin"/>）：页面声明的值能不能用，以及服务端要不要自己给一个。
/// </summary>
/// <remarks>
/// <para>
/// 来源是应用定义的值，说明这段对话是从哪开始的。它有两种出处：
/// 页面在请求里声明（<see cref="ChatRequestDto.Origin"/>，例如「从卡片开始」），或者只有服务端知道（例如这段对话是在一台临时口令设备上开的）。
/// 页面声明的值不能直接信：任何人都能在请求里写一个「Email」，让一段手动开始的对话在列表里显示成邮件规则发起的。
/// 服务端知道的值不该交给页面去声明：页面可以不声明，也可以声明别的。
/// </para>
/// <para>
/// 所以由策略给出最终的值：返回成功时 <c>Data</c> 就是新线程的来源（<c>null</c> = 没有来源），可以是页面声明的那个、服务端自己定的一个（覆盖或代替页面的），
/// 也可以是 <c>null</c>（丢掉页面的声明）；返回失败则这一轮被拒绝。服务端给的值同样要满足 <see cref="ThreadOrigin"/> 的形状，不合规是应用的缺陷，答 500。
/// </para>
/// <para>
/// 默认实现 <see cref="ConfiguredThreadOriginPolicy"/> 只放行 <c>AI:Chat:AllowedThreadOrigins</c> 里的声明（不配 = 一个都不放行），不自己给值。
/// 应用注册自己的实现即可替换它（默认实现以 <c>TryAdd</c> 在 PostConfigure 阶段注册）；它是 Scoped 服务，可以注入当前用户之类判断「这是哪种会话」所需的东西。
/// </para>
/// <para>
/// 由 <c>ChatService</c> 在每一轮新建线程的聊天请求上调用（内建路径与外部 CLI 路径同一处，排在任何写入之前），页面没有声明来源时也调用，
/// 这样服务端才有机会给值；续接已有线程的请求只在页面声明了来源时调用（只看放行与否，值用不上）。
/// 形状（<see cref="ThreadOrigin"/>）在调用前已经校验过。服务端代码自己建线程（<c>IAgentThreadService.CreateAsync</c>、
/// <c>AgentRunRequest.ThreadOrigin</c>）不经过这里：那是应用自己写的值。
/// </para>
/// </remarks>
public interface IThreadOriginPolicy
{
    /// <summary>
    /// 新线程的来源：成功时 <c>Data</c> 是要写进线程的值（<c>null</c> = 不带来源）；
    /// 失败则拒绝这一轮（带状态码与错误码，默认实现：400 <c>AI_THREAD_ORIGIN_NOT_ALLOWED</c>）。
    /// </summary>
    /// <param name="request">这一轮的情况：页面声明的来源、Agent、用户、是否新建线程、每轮元数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<string?>> ResolveAsync(ThreadOriginRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// 交给 <see cref="IThreadOriginPolicy"/> 的一轮聊天的情况。
/// </summary>
/// <param name="DeclaredOrigin">页面声明的来源（<see cref="ChatRequestDto.Origin"/>，形状已合规）；没有声明为 null。</param>
/// <param name="AgentId">请求的 Agent；null = 不带 Agent 的聊天。</param>
/// <param name="UserId">发起聊天的用户；匿名调用为 null。</param>
/// <param name="StartsThread">这一轮会新建线程（请求没有 <c>ThreadId</c>）。为 false 时返回的值不会被使用。</param>
/// <param name="Metadata">这一轮的每轮元数据（<see cref="ChatRequestDto.Metadata"/>，已校验；没有时为空）。</param>
public sealed record ThreadOriginRequest(
    string? DeclaredOrigin,
    Guid? AgentId,
    Guid? UserId,
    bool StartsThread,
    IReadOnlyDictionary<string, JsonElement> Metadata);
