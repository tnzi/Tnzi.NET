namespace Tnzi.AI.Services;

/// <summary>
/// 管理员设的 AI 用量与模型策略：所有人的默认值、某个用户的单独设置，以及据此算出的生效值。
/// </summary>
/// <remarks>
/// <para>
/// 三层逐项取值：用户的单独设置 → 租户的默认值 → 配置文件 <c>AI:UsageLimits</c>。每一项「不设」就跟随下一层，可以显式设成不限。
/// </para>
/// <para>
/// 模型清单在 <c>AI:Chat:Models</c>。没配清单时不提供模型选择：<see cref="GetChatModelsAsync"/> 返回空列表，
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
}
