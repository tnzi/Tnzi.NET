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

    /// <summary>聊天消息附件的上限。</summary>
    public ChatAttachmentOptions Attachments { get; set; } = new();

    /// <summary>每轮元数据（<c>ChatRequestDto.Metadata</c>）的上限。</summary>
    public ChatMetadataOptions Metadata { get; set; } = new();

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
/// 聊天消息附件的上限。
/// </summary>
/// <remarks>
/// 作用于发给绑定了外部 CLI 运行时的 Agent 的消息：那里附件会被校验、随消息存档，并交给 CLI。
/// 内建引擎照旧把内容块直接交给提供商。
/// </remarks>
public class ChatAttachmentOptions
{
    /// <summary>单个附件的最大字节数（解码后）。默认 10 MB。</summary>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>一条消息最多带几个附件。默认 10。</summary>
    public int MaxCount { get; set; } = 10;
}
