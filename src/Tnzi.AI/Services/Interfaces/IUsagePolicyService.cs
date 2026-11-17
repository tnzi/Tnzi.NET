namespace Tnzi.AI.Services;

/// <summary>
/// 管理员设的 AI 用量与模型策略：所有人的默认值、某个用户的单独设置，以及据此算出的生效值。
/// </summary>
/// <remarks>
/// <para>
/// 三层逐项取值：用户的单独设置 → 租户的默认值 → 配置文件 <c>AI:UsageLimits</c>。每一项「不设」就跟随下一层，可以显式设成不限。
/// </para>
/// <para>
/// 模型清单在 <c>AI:Chat:Models</c>。没配清单时不提供模型选择：<see cref="GetChatModelsAsync(Guid?, Guid?, string?, CancellationToken)"/> 返回空列表，
/// <see cref="ResolveChatModelAsync"/> 原样交回请求里的模型。
/// </para>
/// </remarks>
public interface IUsagePolicyService
{
    /// <summary>所有人的默认值（含配置文件那一层与生效值）。</summary>
    Task<Result<UsageLimitDefaultsDto>> GetDefaultsAsync(CancellationToken ct = default);

    /// <summary>保存所有人的默认值。模型不在清单里、默认模型不在允许列表里、选了「限定」却没给金额都答 400 <c>AI_USAGE_POLICY_INVALID</c>。</summary>
    Task<Result<UsageLimitDefaultsDto>> SaveDefaultsAsync(SaveUsagePolicyDto input, CancellationToken ct = default);

    /// <summary>某个用户的单独设置与生效值（<see cref="UserUsageLimitsDto.Usage"/> 不在这里填，见 <see cref="IUsageLimitService.GetStatusAsync"/>）。</summary>
    Task<Result<UserUsageLimitsDto>> GetUserPolicyAsync(Guid userId, CancellationToken ct = default);

    /// <summary>保存某个用户的单独设置。校验规则同 <see cref="SaveDefaultsAsync"/>。</summary>
    Task<Result<UserUsageLimitsDto>> SaveUserPolicyAsync(Guid userId, SaveUsagePolicyDto input, CancellationToken ct = default);

    /// <summary>删掉某个用户的单独设置（每一项回到跟随默认值）。</summary>
    Task<Result> ClearUserPolicyAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// 这个用户生效的上限与模型。<paramref name="userId"/> 为 null 时只取默认值。
    /// </summary>
    Task<EffectiveUsagePolicyDto> ResolveAsync(Guid? userId, CancellationToken ct = default);

    /// <summary>
    /// 一批用户各自有没有单独设置、生效的上限是多少（管理端计量列表用，一次查完）。
    /// </summary>
    Task<IReadOnlyDictionary<Guid, (bool HasCustomPolicy, decimal? SessionLimitUsd, decimal? WeeklyLimitUsd)>> ResolveLimitsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>模型清单（<c>AI:Chat:Models</c>）。</summary>
    IReadOnlyList<ChatModelDto> GetCatalog();

    /// <summary>
    /// 这个用户在这个 Agent 上可以选的模型（允许列表里、属于这个 Agent 的 provider 的那些）与默认选中的那个。
    /// </summary>
    /// <param name="userId">用户；null 时按默认值。</param>
    /// <param name="agentId">Agent；null = 不带 Agent 的聊天，按 <paramref name="provider"/> 或默认 provider 过滤。</param>
    /// <param name="provider">不带 Agent 的聊天请求里的 provider。</param>
    /// <param name="ct">取消令牌。</param>
    Task<Result<ChatModelsDto>> GetChatModelsAsync(Guid? userId, Guid? agentId, string? provider = null, CancellationToken ct = default);

    /// <summary>
    /// 定下这一轮用的模型：请求带了模型就校验它（不在这个用户的允许列表里、或不属于这个 Agent 的 provider，答 403 <c>AI_MODEL_NOT_ALLOWED</c>），
    /// 没带就给这个用户的默认模型。没配模型清单时原样交回 <paramref name="requestedModel"/>。
    /// </summary>
    Task<Result<string?>> ResolveChatModelAsync(Guid? userId, Guid? agentId, string? provider, string? requestedModel, CancellationToken ct = default);

    /// <summary>
    /// 定下这一轮的模型与推理强度：模型同 <see cref="ResolveChatModelAsync"/>；请求带了强度时，它必须是这一轮最终用的模型
    /// （请求没带模型时是默认模型）可选的档位之一（<see cref="ChatModelDto.Efforts"/>，不分大小写，交回清单里的写法），
    /// 否则 403 <c>AI_EFFORT_NOT_ALLOWED</c>；没配模型清单或模型不提供强度选择时，带了强度一律拒绝。没带强度时强度为 null（由绑定或运行时决定）。
    /// </summary>
    /// <remarks>
    /// 默认实现只认得模型：带了强度就拒绝（替换了本接口、还没跟上的实现宁可拒绝，也不让一个没校验过的值交给运行时）。
    /// </remarks>
    async Task<Result<ChatTurnModel>> ResolveChatTurnAsync(
        Guid? userId, Guid? agentId, string? provider, string? requestedModel, string? requestedEffort, CancellationToken ct = default)
    {
        var model = await ResolveChatModelAsync(userId, agentId, provider, requestedModel, ct);
        if (!model.Succeeded)
        {
            return Result<ChatTurnModel>.Failure(model.Message ?? "The model is not available.", model.Code ?? 403, model.ErrorCode ?? ErrorCodes.ModelNotAllowed);
        }

        return string.IsNullOrWhiteSpace(requestedEffort)
            ? Result<ChatTurnModel>.Success(new ChatTurnModel(model.Data, null))
            : Result<ChatTurnModel>.Failure($"The reasoning effort '{requestedEffort.Trim()}' cannot be chosen here.", 403, ErrorCodes.EffortNotAllowed);
    }

    /// <summary>
    /// 同 <see cref="GetChatModelsAsync(Guid?, Guid?, string?, CancellationToken)"/>，并带上对话线程：注册了 <see cref="IChatModelTargetResolver"/> 时，
    /// 按这段对话实际的 provider / 模型列出模型与档位。默认实现忽略线程。
    /// </summary>
    Task<Result<ChatModelsDto>> GetChatModelsAsync(ChatModelsRequest request, CancellationToken ct = default)
        => GetChatModelsAsync(request.UserId, request.AgentId, request.Provider, ct);

    /// <summary>
    /// 同 <see cref="ResolveChatTurnAsync(Guid?, Guid?, string?, string?, string?, CancellationToken)"/>，按 <see cref="GetChatModelsAsync(ChatModelsRequest, CancellationToken)"/>
    /// 列出的模型校验（对话实际落在别家 CLI 上时按那家的模型与档位）。默认实现忽略线程。
    /// </summary>
    Task<Result<ChatTurnModel>> ResolveChatTurnAsync(
        ChatModelsRequest request, string? requestedModel, string? requestedEffort, CancellationToken ct = default)
        => ResolveChatTurnAsync(request.UserId, request.AgentId, request.Provider, requestedModel, requestedEffort, ct);

    /// <summary>
    /// 某家 provider 上某个模型对这个用户提供的推理强度：策略 → 模型清单 → <paramref name="runtimeId"/> 握手时报的档位，第一个声明了的为准。
    /// 调度器在落点把一轮换到别的 provider 或模型之后，用它重新校验这一轮的强度。
    /// </summary>
    /// <returns>可选的档位（可能为空 = 不提供选择）；默认实现返回 null（说不出来，调用方按不合法处理）。</returns>
    Task<IReadOnlyList<string>?> GetModelEffortsAsync(
        Guid? userId, string providerKey, string? modelId, Guid? runtimeId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>?>(null);
}

/// <summary>
/// 列模型、校验一轮时说明「谁、在哪个 Agent 上、哪段对话」。
/// </summary>
/// <param name="UserId">用户；null 时按默认值。</param>
/// <param name="AgentId">Agent；null = 不带 Agent 的聊天，按 <paramref name="Provider"/> 或默认 provider 过滤。</param>
/// <param name="Provider">不带 Agent 的聊天请求里的 provider。</param>
/// <param name="ThreadId">
/// 对话线程：注册了 <see cref="IChatModelTargetResolver"/> 时交给它，按这段对话实际的 provider / 模型列出模型与档位。
/// 不是这个用户的线程按没给处理。
/// </param>
public sealed record ChatModelsRequest(Guid? UserId, Guid? AgentId, string? Provider = null, Guid? ThreadId = null)
{
    /// <summary>
    /// 这一轮的每轮元数据（列模型时是页面将随下一轮发出的那份），已校验；交给 <see cref="IChatModelTargetResolver"/>。null = 没有。
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
}

/// <summary>一轮定下来的模型与推理强度（<see cref="IUsagePolicyService.ResolveChatTurnAsync(Guid?, Guid?, string?, string?, string?, CancellationToken)"/>）。</summary>
/// <param name="Model">模型 id；null = 由 Agent 或绑定决定。</param>
/// <param name="Effort">推理强度（运行时原生值）；null = 这一轮不带强度。</param>
public sealed record ChatTurnModel(string? Model, string? Effort);
