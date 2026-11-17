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
    /// 按手机号解析账号：查无 / 恰好一个 / 不止一个。见 <see cref="PhoneNumberAccountLookup"/>。
    /// </summary>
    /// <remarks>
    /// ★ 刻意不提供返回 <c>User?</c> 的按号查找：那种形状只能在多个账号同号时替用户挑一个，
    /// 而在号码被共用的部署里，挑中的往往是最早建的那个管理员账号。
    /// <paramref name="requireConfirmed"/> 进查询谓词而不是事后过滤：手机号没有唯一约束，
    /// 事后过滤会让一条未验证的重复行把真号主挡在门外。
    /// </remarks>
    internal static Task<PhoneNumberAccountLookup> ResolveByPhoneNumberAsync(this UserManager<User> userManager, string? phoneNumber, bool requireConfirmed = false)
        => PhoneNumberAccountLookup.ResolveAsync(userManager.Users, phoneNumber, requireConfirmed);
}
