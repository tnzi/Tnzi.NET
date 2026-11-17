namespace Tnzi.AI.Services;

/// <summary>
/// 由使用方说出一个用户的一段对话实际会跑在哪个 provider / 模型上，好让模型清单（<c>GET chat/models</c>）与每轮强度的校验
/// 按那里给出模型与档位，而不是按 Agent 绑定的 provider。
/// </summary>
/// <remarks>
/// <para>
/// 用在一轮的 CLI 由使用方在落点时才定的场景（<c>ICliRunPlacementResolver</c> 按用户的 CLI 档案把绑定在 claude 上的 Agent
/// 换到 codex 或 kimi）：不实现它时，清单按 Agent 绑定的 provider 给出，用户看到的是绑定那家的模型与档位，选的强度换家之后没有意义。
/// 实现它的消费方应与自己的落点解析器给出同一个答案（同一个档案、同一条规则）。
/// </para>
/// <para>
/// 按用户、线程与这一轮的每轮元数据（用户在输入框旁的「运行选择」）回答。列模型清单、校验聊天请求的模型与强度时各调用一次（入队之后落点换了 provider 时，调度器按落点的那家再校验一次强度）。
/// 返回 null（或全部字段为空）= 按 Agent 绑定。在消费方模块的 <c>ConfigureServicesAsync</c> 里注册为 Scoped。
/// </para>
/// </remarks>
[ExperimentalApi(Reason = "Chat model lists that follow a consumer-chosen CLI are new")]
public interface IChatModelTargetResolver
{
    /// <summary>这一段对话实际的 provider / 模型；null = 按绑定。</summary>
    Task<ChatModelTarget?> ResolveAsync(ChatModelTargetRequest request, CancellationToken cancellationToken = default);
}

/// <summary>交给 <see cref="IChatModelTargetResolver"/> 的信息。</summary>
public sealed record ChatModelTargetRequest
{
    /// <summary>用户；没有登录用户的进程内调用为 null。</summary>
    public Guid? UserId { get; init; }

    /// <summary>Agent。</summary>
    public required Guid AgentId { get; init; }

    /// <summary>对话线程；还没有线程（新对话）时为 null。线程归属在调用之前已经核过（不是这个用户的线程不会交到这里）。</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>Agent 绑定的 provider 键（例如 <c>claude</c>）；Agent 不走外部执行时为 null。</summary>
    public string? BindingProviderKey { get; init; }

    /// <summary>
    /// 这一轮的每轮元数据（<c>ChatRequestDto.Metadata</c>，已按 <c>AI:Chat:Metadata</c> 校验）；列模型清单时是页面说它将随下一轮发出的那份
    /// （<c>GET chat/models?metadata=</c>）。没有时为空字典。
    /// </summary>
    /// <remarks>
    /// 还没有线程的时候（新对话、起始页、自动化表单），用户在输入框旁选的「运行选择」（例如 CLI 档案）只在这里：按它回答，
    /// 页面预览的模型与档位就和这一轮实际校验的一致（两处收到的是同一份元数据）。有线程又带了选择时，以选择为准还是以线程为准由应用决定。
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; } = EmptyMetadata;

    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyMetadata = new Dictionary<string, JsonElement>();
}

/// <summary>
/// 一段对话实际的落点。每一项为空都表示「按绑定」。
/// </summary>
public sealed record ChatModelTarget
{
    /// <summary>
    /// 实际的 provider 键（例如 <c>codex</c>、<c>kimi</c>）：模型清单只列 <c>AI:Chat:Models</c> 里属于它的（没写 provider 的照旧都列）。
    /// </summary>
    public string? ProviderKey { get; init; }

    /// <summary>
    /// 这段对话固定用的模型（例如用户档案里定的）；给了就只列它一个、并作默认，不在模型清单里的也照样列出（名字取 <see cref="ModelDisplayName"/>）。
    /// 空 = 列出属于 <see cref="ProviderKey"/> 的、这个用户可用的模型，由用户选。
    /// </summary>
    public string? Model { get; init; }

    /// <summary><see cref="Model"/> 不在模型清单里时显示的名字；空 = 显示模型 id。</summary>
    public string? ModelDisplayName { get; init; }

    /// <summary>
    /// 实际执行的运行时（<c>AI_CliRuntime</c> 的 id）：策略与模型清单都没给档位的模型取它握手时报的档位（ACP 的 <c>thought_level</c>）。
    /// 空 = provider 与绑定相同时用绑定的运行时，不同时没有握手档位可用。
    /// </summary>
    public Guid? RuntimeId { get; init; }
}
