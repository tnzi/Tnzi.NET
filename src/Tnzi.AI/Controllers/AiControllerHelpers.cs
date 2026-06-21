namespace Tnzi.AI.Controllers;

/// <summary>
/// AI 用户端控制器共享辅助方法 — 集中 GetCurrentUserId 等重复逻辑。
/// </summary>
internal static class AiControllerHelpers
{
    /// <summary>
    /// 从已认证的 <see cref="ICurrentUser"/> 提取用户 ID，未认证（无 Id）时抛出 401。
    /// 用户可见错误消息为英文，错误码统一使用框架的 <see cref="Tnzi.Exceptions.ErrorCodes.UNAUTHORIZED"/>。
    /// </summary>
    public static Guid RequireUserId(ICurrentUser currentUser)
    {
        Check.NotNull(currentUser);
        var userId = currentUser.Id;
        if (!userId.HasValue)
            throw new BusinessException("Authentication required", Tnzi.Exceptions.ErrorCodes.UNAUTHORIZED, 401);
        return userId.Value;
    }
}
