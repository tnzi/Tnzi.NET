
namespace Tnzi.Identity.Services;

/// <summary>
/// 注册服务接口
/// </summary>
public interface IRegistrationService
{
    /// <summary>
    /// 用户注册（注册成功后自动登录并返回Token）
    /// </summary>
    Task<Result<TokenResult>> RegisterAsync(RegisterDto input);

    /// <summary>
    /// 发送快速注册验证码
    /// </summary>
    Task<Result<string>> SendQuickRegisterCodeAsync(SendQuickRegisterCodeDto input);

    /// <summary>
    /// 快速注册（无需密码）
    /// </summary>
    Task<Result<QuickRegisterResultDto>> QuickRegisterAsync(QuickRegisterDto input);

    /// <summary>
    /// 设置密码（快速注册后使用）
    /// </summary>
    Task<Result<string>> SetPasswordAsync(SetPasswordDto input);

    /// <summary>
    /// 生成邮箱确认令牌
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>邮箱确认令牌（URL安全的Base64编码）</returns>
    Task<Result<string>> GenerateEmailConfirmationTokenAsync(Guid userId);

    /// <summary>
    /// 确认邮箱
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="token">邮箱确认令牌</param>
    /// <returns>操作结果</returns>
    Task<Result> ConfirmEmailAsync(Guid userId, string token);

    /// <summary>
    /// 重发邮箱确认邮件
    /// </summary>
    /// <param name="input">重发请求（包含用户ID或邮箱）</param>
    /// <returns>操作结果</returns>
    Task<Result<string>> ResendEmailConfirmationAsync(ResendEmailConfirmationDto input);
}
