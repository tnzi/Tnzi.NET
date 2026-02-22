namespace Tnzi.Hosting.Controllers;

/// <summary>
/// 默认用户通知控制器（收件箱）
/// 路由：/api/notifications（自动添加 /api 前缀）
/// 用户项目可以实现 NotificationController 继承此 Controller，然后重写方法
/// </summary>
public class DefaultNotificationController : NotificationControllerBase
{
    public DefaultNotificationController(IUserNotificationService userNotificationService)
        : base(userNotificationService)
    {
    }
}
