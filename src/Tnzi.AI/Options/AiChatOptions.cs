namespace Tnzi.AI.Options;

/// <summary>
/// 聊天端点的部署级规则，绑定 <c>AI:Chat</c> 配置节。
/// </summary>
[ConfigSection("AI:Chat")]
public class AiChatOptions
{
    /// <summary>
    /// 用户能对话的 Agent。<b>空 = 不限</b>（任何对调用者可见且已启用的 Agent，与此前行为相同）。
    /// </summary>
    /// <remarks>
    /// 由默认的 <c>IChatAgentPolicy</c> 执行；需要按用户、按角色决定的消费方替换那个接口。
    /// 只管聊天端点（<c>chat</c>、<c>chat/stream</c> 与编辑、重新生成）：管理端的 Agent 运行端点由权限码管着，不在此列。
    /// </remarks>
    public List<Guid> AllowedAgentIds { get; set; } = [];

    /// <summary>
    /// 是否允许不带 AgentId 的聊天（直接指定 provider / model 的裸调用）。<b>默认 true</b>（与此前行为相同）。
    /// 只想让用户与自己的 Agent 对话的部署把它关掉，否则用户换一个不带 AgentId 的请求就绕过了 <see cref="AllowedAgentIds"/>。
    /// </summary>
    public bool AllowAgentlessChat { get; set; } = true;

    /// <summary>
    /// 客户端在聊天请求里可以声明的线程来源（<c>ChatRequestDto.Origin</c>）。<b>空 = 客户端不能声明任何来源</b>。
    /// </summary>
    /// <remarks>
    /// 由默认的 <c>IThreadOriginPolicy</c> 执行，按序数（大小写敏感）比较；需要按用户、按 Agent 决定，或要由服务端给出来源的消费方替换那个接口。
    /// 只管客户端发来的值：服务端代码建线程（<c>IAgentThreadService.CreateAsync</c>、<c>AgentRunRequest.ThreadOrigin</c>）
    /// 写什么由应用自己负责，不经过这张清单。
    /// </remarks>
    public List<string> AllowedThreadOrigins { get; set; } = [];

    /// <summary>聊天消息附件的上限。</summary>
    public ChatAttachmentOptions Attachments { get; set; } = new();

    /// <summary>每轮元数据（<c>ChatRequestDto.Metadata</c>）的上限。</summary>
    public ChatMetadataOptions Metadata { get; set; } = new();

    /// <summary>线程的消息队列（一轮还在跑时写下的消息，在它结束时合成一轮发出）。</summary>
    public ChatQueueOptions Queue { get; set; } = new();

    /// <summary>
    /// 用户在聊天界面里可以选的模型清单。<b>空 = 不提供模型选择</b>（与此前行为相同）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 配了清单之后，聊天请求的 <c>Model</c> 必须是这个用户被允许的模型之一（管理员按用户或给所有人设，见 <c>AI:UsageLimits:AllowedModels</c>），
    /// 不在其中答 403 <c>AI_MODEL_NOT_ALLOWED</c>；请求没带模型时用这个用户的默认模型。选中的模型真的用于这一轮：
    /// 内建 Agent 换成这个模型，绑了外部 CLI 运行时的 Agent 以 <c>--model</c> 交给 CLI。
    /// </para>
    /// <para>
    /// 没配清单时，绑了外部 CLI 运行时的 Agent 仍忽略请求里的模型（由绑定决定）：没有允许列表就放开它，任何用户都能点名最贵的那个。
    /// </para>
    /// </remarks>
    public List<ChatModelOption> Models { get; set; } = [];
}

/// <summary>
/// 模型清单里的一项。
/// </summary>
public class ChatModelOption
{
    /// <summary>模型 id，原样交给 provider（例如 <c>claude-sonnet-5-5</c>）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名（例如 <c>Sonnet 5.5</c>）；不填时显示 <see cref="Id"/>。</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 这个模型属于哪个 provider：绑了外部 CLI 运行时的 Agent 比的是运行时的 provider 键（例如 <c>claude</c>），
    /// 内建 Agent 比的是它的 AI provider 名（<c>AI:Providers</c> 的键或数据库里的 Provider 名）。
    /// 不填 = 哪个 Agent 都能用。
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>一句说明（选择器里显示在名字下面），可不填。</summary>
    public string? Description { get; set; }

    /// <summary>
    /// 这个模型可选的推理强度，运行时原生值，原样交给运行时（例如 Claude Code 的 <c>low</c> / <c>medium</c> / <c>high</c> / <c>xhigh</c> / <c>max</c>）。
    /// 不填 = 这一层不声明：管理端的策略也没设时，取绑定的外部 CLI 运行时握手时报的档位，都没有就不提供强度选择。
    /// </summary>
    /// <remarks>
    /// 各家 CLI 的词表不同，框架刻意不做归一化。外部 CLI 运行时上，stream-json 运行时以 <c>--effort</c> 收下，
    /// ACP 运行时以 <c>thought_level</c> 会话选项收下；内建 Agent 上，能按名字读成 <c>ReasoningEffort</c>（none / low / medium / high / max）的值才生效。
    /// </remarks>
    public List<string> Efforts { get; set; } = [];

    /// <summary>默认选中的强度，必须是 <see cref="Efforts"/> 之一；不填 = 不预先选（这一轮不带强度，由运行时或绑定决定）。</summary>
    public string? DefaultEffort { get; set; }
}

/// <summary>
/// 每轮元数据的上限。
/// </summary>
/// <remarks>
/// 元数据是消费方随一轮带来的一小组值（例如这条消息点名的几台设备的 id），随用户消息存下、重新生成时原样再发一次，
/// 所以它该保持「小」：要传大块内容的，放进消息正文或附件。
/// </remarks>
public class ChatMetadataOptions
{
    /// <summary>最多几个键。默认 16。</summary>
    public int MaxKeys { get; set; } = 16;

    /// <summary>序列化成 JSON 后的最大字节数（UTF-8）。默认 4096。</summary>
    public int MaxBytes { get; set; } = 4096;
}

/// <summary>
/// 线程消息队列的上限与扫描节奏，绑定 <c>AI:Chat:Queue</c>。
/// </summary>
/// <remarks>
/// 队列由运行结束的事件（<c>CliRunFinishedEvent</c>）推动；扫描只为补上事件丢掉的那几次（服务在一轮结束前后重启），
/// 所以间隔不必短。
/// </remarks>
public class ChatQueueOptions
{
    /// <summary>一条线程里最多排几条消息。默认 20。</summary>
    public int MaxMessagesPerThread { get; set; } = 20;

    /// <summary>一条排队消息正文的最大字符数。默认 20000。</summary>
    public int MaxTextLength { get; set; } = 20000;

    /// <summary>扫描的间隔。默认 30 秒。</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 不等任何一轮的消息（排队时线程上没有在跑的）本该当场发出；过了这么久还在等，说明当场发出的那次没做完（服务停了），扫描替它发。默认 1 分钟。
    /// </summary>
    public TimeSpan StrandedAfter { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 发出途中的消息过了这么久还没落定，说明服务在发出途中停了：不知道发没发出去，扣住交给用户决定，不自动重发。默认 5 分钟。
    /// </summary>
    public TimeSpan DeliveringTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 合成一轮时取最严值的每轮元数据键：键 → 这个键的值，<b>越靠前越严</b>（例如 <c>"permissionMode": ["plan", "ask", "full"]</c>）。
    /// 默认空 = 所有键按写下的先后合并、后写的为准。
    /// </summary>
    /// <remarks>
    /// 权限一类的键必须列在这里（或者由应用自己的 <c>IChatQueueMetadataMerger</c> 处理）：否则用户先排一条「先出计划」、再排一条「全权」，
    /// 合成的这一轮以全权运行，第一条上的限制被绕过。不在清单里的值、不是字符串的值按最严算。
    /// </remarks>
    public Dictionary<string, List<string>> StrictestValues { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 聊天消息附件的上限。
/// </summary>
/// <remarks>
/// 作用于发给绑定了外部 CLI 运行时的 Agent 的消息：那里附件会被校验、随消息存档，并交给 CLI
/// （图片与 PDF 随提示投递；收文件的一轮里，其余文件由 agent 用工具打开）。内建引擎照旧把内容块直接交给提供商。
/// </remarks>
public class ChatAttachmentOptions
{
    /// <summary>单个随提示交给模型的附件（图片、PDF）的最大字节数（解码后）。默认 10 MB。</summary>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// 单个文件附件（不是图片或 PDF、不交给模型、由 agent 用自己的工具打开的文件）的最大字节数（解码后）。默认 25 MB。
    /// </summary>
    /// <remarks>
    /// 只在这一轮收文件时用到（<c>ICliFileAttachmentPolicy</c> 放行的一轮）。文件不进模型的上下文，所以与 <see cref="MaxBytes"/> 分开；
    /// 字节随消息存在数据库里，请求体还要再大三分之一（base64），宿主的请求体上限要放得下它。
    /// </remarks>
    public long MaxFileBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>一条消息最多带几个附件。默认 10。</summary>
    public int MaxCount { get; set; } = 10;
}
