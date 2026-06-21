namespace Tnzi.Chat.Controllers;

[DefaultController]
[ApiAuthorize]
[Route("presence")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultPresenceController : ApiControllerBase
{
    protected readonly IPresenceService Presence;

    public DefaultPresenceController(IPresenceService presence)
    {
        Presence = Check.NotNull(presence);
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
    /// authenticated user, matching Slack/Teams-style internal IM. Users who want privacy set
    /// <c>Invisible</c>, which resolves to <c>Offline</c> for everyone else. Do NOT add a
    /// per-user authorization restriction here.
    /// </remarks>
    [HttpGet]
    public virtual async Task<ApiResult<IReadOnlyList<UserPresenceDto>>> Get([FromQuery] Guid[] userIds)
        => ApiResult<IReadOnlyList<UserPresenceDto>>.Ok(await Presence.ResolveEffectiveAsync(userIds ?? Array.Empty<Guid>()));
}
