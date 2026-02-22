namespace Tnzi.Identity.Services;

/// <summary>
/// Identity 页面生成服务接口
/// 提供 OAuth 回调、邮箱确认、密码重置等 HTML 页面的生成
/// </summary>
public interface IIdentityPageService
{
    /// <summary>
    /// 生成 OAuth 回调 HTML 页面（通过 postMessage 传递结果给父窗口）
    /// </summary>
    string GenerateOAuthCallbackHtml(OAuthCallbackResultDto result, string? returnUrl = null);

    /// <summary>
    /// 生成 OAuth 错误 HTML 页面
    /// </summary>
    string GenerateOAuthErrorHtml(string errorMessage);

    /// <summary>
    /// 生成邮箱确认结果 HTML 页面
    /// </summary>
    string GenerateEmailConfirmationResultHtml(bool success, string message);

    /// <summary>
    /// 生成重置密码表单 HTML 页面
    /// </summary>
    string GenerateResetPasswordFormHtml(string email, string token);

    /// <summary>
    /// 生成重置密码结果 HTML 页面
    /// </summary>
    string GenerateResetPasswordResultHtml(bool success, string message);
}
