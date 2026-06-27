
namespace Tnzi.Identity.Services;

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 登录
    /// </summary>
    Task<Result<string>> LoginAsync(LoginDto input);

    /// <summary>
    /// 登录（返回Token结果，包含RefreshToken）
    /// </summary>
    Task<Result<TokenResult>> LoginWithRefreshTokenAsync(LoginDto input);

    /// <summary>
    /// 刷新Token
    /// </summary>
    Task<Result<TokenResult>> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// 登出
    /// </summary>
    Task<Result<string>> LogoutAsync(Guid userId);

    /// <summary>
    /// 发送2FA验证码（登录时）
    /// </summary>
    Task<Result<TwoFactorChallengeDto>> SendTwoFactorCodeAsync(SendTwoFactorCodeDto input);

    /// <summary>
    /// 验证2FA并登录
    /// </summary>
    Task<Result<TokenResult>> VerifyTwoFactorAndLoginAsync(VerifyTwoFactorDto input);

    #region 验证码登录

    /// <summary>
    /// 发送验证码登录验证码
    /// </summary>
    /// <param name="input">发送请求（邮箱或手机号 + 类型）</param>
    /// <returns>操作结果</returns>
    Task<Result<string>> SendCodeLoginCodeAsync(SendCodeLoginCodeDto input);

    /// <summary>
    /// 验证码登录（首次登录如果用户不存在且快速注册开启则自动注册）
    /// </summary>
    /// <param name="input">登录请求（邮箱/手机号 + 验证码 + 类型）</param>
    /// <returns>登录结果（包含 Token 和是否需要设置密码）</returns>
    Task<Result<CodeLoginResultDto>> CodeLoginAsync(CodeLoginDto input);

    #endregion

    #region 验证码找回密码

    /// <summary>
    /// 发送密码找回验证码
    /// </summary>
    /// <param name="input">发送请求（邮箱或手机号 + 类型）</param>
    /// <returns>操作结果</returns>
    Task<Result<string>> SendPasswordRecoveryCodeAsync(SendPasswordRecoveryCodeDto input);

    /// <summary>
    /// 验证码重置密码
    /// </summary>
    /// <param name="input">重置请求（邮箱/手机号 + 验证码 + 新密码 + 类型）</param>
    /// <returns>操作结果</returns>
    Task<Result<string>> ResetPasswordByCodeAsync(ResetPasswordByCodeDto input);

    #endregion

    #region 认证配置

    /// <summary>
    /// 获取公开认证配置（登录方式 / 注册 / 找回 / 第三方登录的开关），供登录页按部署配置渲染。
    /// 只读现有配置选项，仅返回布尔开关与已启用的第三方提供商，不含任何密钥。
    /// </summary>
    /// <returns>认证配置</returns>
    Result<AuthConfigDto> GetAuthConfig();

    #endregion
}
