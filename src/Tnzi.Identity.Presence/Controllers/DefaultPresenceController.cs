namespace Tnzi.Identity.Presence.Controllers;

/// <summary>
/// 用户在线状态端点。任何已认证用户可读（开放目录模型，参见 <see cref="Get"/> 备注）。
/// 独立于 Chat：任何加载了 Presence 模块的应用都可直接使用。
/// </summary>
[DefaultController]
[ApiAuthorize]
[Route("presence")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultPresenceController : ApiControllerBase
{
    protected readonly IPresenceService Presence;
    protected readonly IPresenceConfigService Config;

    public DefaultPresenceController(IPresenceService presence, IPresenceConfigService config)
    {
        Presence = Check.NotNull(presence);
        Config = Check.NotNull(config);
    }

    [HttpPut]
    public virtual async Task<ApiResult> SetStatus([FromBody] SetPresenceDto input)
        => (await Presence.SetStatusAsync(input.Status)).ToApiResult();

    [HttpGet("me")]
    public virtual async Task<ApiResult<UserPresenceStatus>> GetMine()
        => ApiResult<UserPresenceStatus>.Ok(await Presence.GetMyStatusAsync());

    /// <summary>
    /// Resolve effective presence for a batch of users.
    /// </summary>
    /// <remarks>
    /// Intentional open-directory design (not an oversight): presence is readable by any
    /// authenticated user, matching Slack/Teams-style internal directories. Users who want privacy
    /// set <c>Invisible</c>, which resolves to <c>Offline</c> for everyone else. Do NOT add a
    /// per-user authorization restriction here.
    /// </remarks>
    [HttpGet]
    public virtual async Task<ApiResult<IReadOnlyList<UserPresenceDto>>> Get([FromQuery] Guid[] userIds)
        => ApiResult<IReadOnlyList<UserPresenceDto>>.Ok(await Presence.ResolveEffectiveAsync(userIds ?? Array.Empty<Guid>()));

    /// <summary>auto-away 活动上报：从空闲恢复（active=true）或越过本地空闲阈值（active=false）。</summary>
    [HttpPost("activity")]
    public virtual async Task<ApiResult> ReportActivity([FromBody] PresenceActivityDto? input)
        => (await Presence.ReportActivityAsync(input?.Active ?? true)).ToApiResult();

    /// <summary>presence 客户端配置（auto-away 阈值、隐身开关等），供前端本地计时与 UI 门控。</summary>
    [HttpGet("config")]
    public virtual async Task<ApiResult<PresenceClientConfigDto>> GetConfig()
        => (await Config.GetClientConfigAsync()).ToApiResult();
}
