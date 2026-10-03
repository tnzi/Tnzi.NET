namespace Tnzi.Notification.Controllers.Admin;

/// <summary>
/// 退订名单的管理面：分页查、手工登记、按 id 撤销。
/// </summary>
/// <remarks>
/// <para>
/// 退订本身早就在发送路径上生效，但登记完拿不出来等于没登记：合规问询问的是「这个地址何时经哪个渠道退订」
/// 「导出抑制名单」，客户来电说的是「我误点了请恢复」，服务商转来的投诉要人工补录 —— 没有这一面，这些一个都办不了。
/// </para>
/// <para>
/// 与 <c>admin/notification-preferences</c> 是两套东西：偏好按<b>用户</b>记「我选择了什么」，
/// 退订按<b>地址</b>记「这个地址明确说过不要」，前者可被后者否决。别合并。
/// </para>
/// </remarks>
[DefaultController]
[Route("admin/notification-opt-outs")]
[ApiAuthorize(PermissionName = "notification.optOut.view")]
public class DefaultNotificationOptOutAdminController : ApiAdminControllerBase
{
    protected readonly INotificationOptOutService OptOutService;

    public DefaultNotificationOptOutAdminController(INotificationOptOutService optOutService)
    {
        OptOutService = Check.NotNull(optOutService);
    }

    /// <summary>
    /// 分页查询退订名单（按地址包含 / 渠道 / 分类 / 时间段）。
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<OptOutDto>>> GetList([FromQuery] OptOutQueryDto query, CancellationToken cancellationToken)
    {
        var result = await OptOutService.GetPagedListAsync(query, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 手工登记一条退订（服务商投诉、客户来电、导入的黑名单）。幂等：已存在则返回那一条。
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "notification.optOut.create")]
    public virtual async Task<ApiResult<OptOutDto>> Create([FromBody] CreateOptOutDto input, CancellationToken cancellationToken)
    {
        var result = await OptOutService.RegisterAsync(input, cancellationToken);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销一条退订（客户来电说误点了）。撤销后该地址从下一次发送起恢复接收。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "notification.optOut.delete")]
    public virtual async Task<ApiResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await OptOutService.RemoveAsync(id, cancellationToken);
        return result.ToApiResult();
    }
}
