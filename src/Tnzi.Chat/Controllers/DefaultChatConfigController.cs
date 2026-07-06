namespace Tnzi.Chat.Controllers;

[DefaultController]
[ApiAuthorize]
[Route("chat")]
[ApiExplorerSettings(GroupName = "user")]
public class DefaultChatConfigController : ApiControllerBase
{
    protected readonly IChatConfigService Config;

    public DefaultChatConfigController(IChatConfigService config)
    {
        Config = Check.NotNull(config);
    }

    /// <summary>客户端功能配置（前端聊天窗按此显隐群聊/附件/在线状态等入口）。</summary>
    [HttpGet("config")]
    public virtual ApiResult<ChatClientConfigDto> GetConfig()
        => Config.GetClientConfig().ToApiResult();
}
