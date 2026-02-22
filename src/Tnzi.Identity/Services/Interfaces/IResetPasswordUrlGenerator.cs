namespace Tnzi.Identity.Services.Interfaces;

/// <summary>
/// 密码重置URL生成器接口
/// 用户可实现此接口来自定义URL生成逻辑
/// </summary>
public interface IResetPasswordUrlGenerator
{
    /// <summary>
    /// 生成密码重置URL
    /// </summary>
    /// <param name="email">用户邮箱</param>
    /// <param name="token">重置令牌</param>
    /// <param name="frontendUrl">前端URL（如果配置）</param>
    /// <param name="apiBaseUrl">API基础URL</param>
    /// <returns>重置密码URL</returns>
    string GenerateUrl(string email, string token, string? frontendUrl, string? apiBaseUrl);
}

