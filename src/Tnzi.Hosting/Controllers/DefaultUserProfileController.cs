
namespace Tnzi.Hosting.Controllers;

/// <summary>
/// 默认用户个人资料控制器
/// 路由：/api/users/profile（自动添加 /api 前缀）
/// 用户项目可以实现 UserProfileController 继承此 Controller，然后重写方法
/// </summary>
[Route("users/profile")]
public class DefaultUserProfileController : UserProfileControllerBase
{
    public DefaultUserProfileController(
        IUserService userService,
        IPasswordService passwordService,
        ISessionService? sessionService = null,
        IUserLoginService? userLoginService = null,
        ITwoFactorService? twoFactorService = null,
        ILoginLogService? loginLogService = null,
        IUserDetailService? userDetailService = null,
        IOAuthService? oAuthService = null)
        : base(userService, passwordService, sessionService, userLoginService, twoFactorService, loginLogService, userDetailService, oAuthService)
    {
    }
}