using Microsoft.AspNetCore.Mvc.Filters;
using Tnzi.Exceptions;

namespace Tnzi.Chat.Filters;

/// <summary>
/// 用户端聊天端点的访问门：未持 <c>chat.use</c> 的用户，其会话 / 联系人 / 在线状态操作
/// 一律 403。挂在 Conversation / ChatContact / Presence 控制器；<b>刻意不挂</b>
/// ChatConfig（前端需读 <c>GET /chat/config</c> 才能知道自己被禁）与 admin 控制器。
/// Authorization 未加载时 <see cref="IChatAccessService"/> fail-open，本门自动放行。
/// </summary>
public sealed class ChatAccessGuardFilter : IAsyncActionFilter
{
    private readonly IChatAccessService _access;

    public ChatAccessGuardFilter(IChatAccessService access)
    {
        _access = Check.NotNull(access);
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!await _access.CanCurrentUserUseAsync())
            throw new ForbiddenException("Chat is not enabled for your account.");

        await next();
    }
}
