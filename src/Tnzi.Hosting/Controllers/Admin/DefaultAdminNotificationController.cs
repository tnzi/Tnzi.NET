namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员通知管理控制器
/// 路由：/api/admin/notifications（自动添加 /api 前缀）
/// 用户项目可以实现 AdminNotificationController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/notifications")]
public class DefaultAdminNotificationController : NotificationAdminControllerBase
{
    public DefaultAdminNotificationController(
        INotificationService notificationService,
        INotificationQueryService queryService,
        INotificationRetryService retryService)
        : base(notificationService, queryService, retryService)
    {
    }
}
