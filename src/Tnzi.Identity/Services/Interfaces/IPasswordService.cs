
namespace Tnzi.Identity.Services;

/// <summary>
/// 密码服务接口
/// </summary>
public interface IPasswordService
{
    /// <summary>
    /// 忘记密码（发送重置链接）
    /// </summary>
    Task<Result<string>> ForgotPasswordAsync(string email);

    /// <summary>
    /// 通过Token重置密码（用户忘记密码流程）
    /// </summary>
    Task<Result<string>> ResetPasswordByTokenAsync(string email, string token, string newPassword);

    /// <summary>
    /// 修改密码（用户主动修改，需要当前密码）
    /// </summary>
    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);

    /// <summary>
    /// 重置密码（管理员重置，不需要当前密码）
    /// </summary>
    Task<Result> ResetPasswordByAdminAsync(Guid userId, string newPassword);
}
