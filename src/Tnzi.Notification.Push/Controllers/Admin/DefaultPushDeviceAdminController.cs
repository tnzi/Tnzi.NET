namespace Tnzi.Notification.Push.Controllers.Admin;

/// <summary>
/// 推送设备注册表的 admin 面：查清单、删掉一台。
/// </summary>
/// <remarks>
/// 没有创建与修改端点，理由见 <see cref="Permissions.NotificationPushPermissions"/>：
/// 设备数据全部由客户端上报，运维手工写进去的行不对应任何一台真实设备。
/// </remarks>
[DefaultController]
[Route("admin/notification-devices")]
[ApiAuthorize(PermissionName = "notification.pushDevice.view")]
public class DefaultPushDeviceAdminController : ApiAdminControllerBase
{
    protected readonly IPushDeviceService DeviceService;

    public DefaultPushDeviceAdminController(IPushDeviceService deviceService)
    {
        DeviceService = Check.NotNull(deviceService);
    }

    /// <summary>分页查询已注册设备。令牌以掩码返回，见 <see cref="PushDeviceDto.TokenMask"/>。</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<PushDeviceDto>>> GetList([FromQuery] PushDeviceQueryDto query)
        => (await DeviceService.GetPagedListAsync(query)).ToApiResult();

    /// <summary>删除一台设备（例如用户报失、或确认某台已不再使用）。</summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "notification.pushDevice.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
        => (await DeviceService.DeleteAsync(id)).ToApiResult();
}
