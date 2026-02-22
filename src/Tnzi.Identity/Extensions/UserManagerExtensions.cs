namespace Tnzi.Identity.Extensions;

/// <summary>
/// UserManager 扩展方法
/// 提供 Guid 类型 userId 的查找方法，消除各服务中的重复代码
/// </summary>
internal static class UserManagerExtensions
{
    /// <summary>
    /// 根据 Guid 类型的用户ID查找用户
    /// </summary>
    internal static Task<User?> FindByGuidAsync(this UserManager<User> userManager, Guid userId)
        => userManager.FindByIdAsync(userId.ToString());

    /// <summary>
    /// 根据手机号查找用户
    /// </summary>
    internal static async Task<User?> FindByPhoneNumberAsync(this UserManager<User> userManager, string? phoneNumber, bool requireConfirmed = false)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber)) return null;
        var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
        if (requireConfirmed && user != null && !user.PhoneNumberConfirmed)
        {
            return null;
        }
        return user;
    }
}
