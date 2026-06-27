namespace Tnzi.Chat.Controllers;

[DefaultController]
[ApiAuthorize]
[Route("chat/contacts")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultChatContactController : ApiControllerBase
{
    protected readonly IChatContactService Contacts;

    public DefaultChatContactController(IChatContactService contacts)
    {
        Contacts = Check.NotNull(contacts);
    }

    [HttpGet("search")]
    public virtual async Task<ApiResult<IReadOnlyList<ChatContactDto>>> Search([FromQuery] string? keyword)
        => (await Contacts.SearchUsersAsync(keyword)).ToApiResult();

    [HttpGet("{userId:guid}/profile")]
    public virtual async Task<ApiResult<ChatContactProfileDto>> GetProfile(Guid userId)
        => (await Contacts.GetProfileAsync(userId)).ToApiResult();
}
