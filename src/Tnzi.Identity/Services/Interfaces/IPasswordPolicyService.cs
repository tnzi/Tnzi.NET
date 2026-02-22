


namespace Tnzi.Identity.Services;

/// <summary>
/// 密码策略服务接口
/// </summary>
public interface IPasswordPolicyService
{
    /// <summary>
    /// 验证密码强度
    /// </summary>
    /// <param name="password">密码</param>
    /// <returns>错误消息，如果验证通过则返回null</returns>
    string? ValidatePasswordStrength(string password);

    /// <summary>
    /// 检查密码是否在历史记录中
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="newPassword">新密码</param>
    /// <returns>如果密码在历史中返回true</returns>
    Task<bool> CheckPasswordHistoryAsync(Guid userId, string newPassword);

    /// <summary>
    /// 保存密码历史
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="passwordHash">密码哈希值</param>
    Task SavePasswordHistoryAsync(Guid userId, string passwordHash);

    /// <summary>
    /// 检查用户密码是否已过期
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>密码过期检查结果</returns>
    Task<PasswordExpirationResult> CheckPasswordExpirationAsync(Guid userId);

    /// <summary>
    /// 获取用户上次修改密码的时间
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>上次修改时间，如果从未修改则返回null</returns>
    Task<DateTime?> GetLastPasswordChangeTimeAsync(Guid userId);
}

/// <summary>
/// 密码过期检查结果
/// </summary>
public class PasswordExpirationResult
{
    /// <summary>
    /// 密码是否已过期
    /// </summary>
    public bool IsExpired { get; set; }

    /// <summary>
    /// 密码过期日期（如果已过期）
    /// </summary>
    public DateTime? ExpiredAt { get; set; }

    /// <summary>
    /// 距离过期的天数（负数表示已过期天数）
    /// </summary>
    public int? DaysUntilExpiration { get; set; }

    /// <summary>
    /// 创建未过期的结果
    /// </summary>
    public static PasswordExpirationResult NotExpired(int? daysUntilExpiration = null)
        => new() { IsExpired = false, DaysUntilExpiration = daysUntilExpiration };

    /// <summary>
    /// 创建已过期的结果
    /// </summary>
    public static PasswordExpirationResult Expired(DateTime expiredAt)
        => new() { IsExpired = true, ExpiredAt = expiredAt, DaysUntilExpiration = -(int)(DateTime.UtcNow - expiredAt).TotalDays };

    /// <summary>
    /// 创建不需要检查的结果（密码过期功能未启用）
    /// </summary>
    public static PasswordExpirationResult NotRequired()
        => new() { IsExpired = false };
}
