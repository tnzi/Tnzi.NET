using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Hosting.Controllers;

/// <summary>
/// 默认认证控制器
/// 路由：/api/auth（自动添加 /api 前缀）
/// 提供登录、注册、忘记密码等公开 API
/// 用户项目可以实现 AuthController 继承此 Controller，然后重写方法
/// </summary>
[ApiController]
[Route("auth")]
public class DefaultAuthController : AuthControllerBase
{
    public DefaultAuthController(
        ITwoFactorService twoFactorService,
        IAuthService authService,
        IRegistrationService registrationService,
        IPasswordService passwordService,
        IOAuthService? oAuthService = null,
        ICaptchaService? captchaService = null,
        IOptions<IdentityOptions>? identityOptions = null,
        IConfiguration? configuration = null,
        IIdentityPageService? identityPageService = null,
        IPasswordPolicyService? passwordPolicyService = null)
        : base(twoFactorService, authService, registrationService, passwordService,
               oAuthService, captchaService, identityOptions, configuration, identityPageService,
               passwordPolicyService)
    {
    }
}