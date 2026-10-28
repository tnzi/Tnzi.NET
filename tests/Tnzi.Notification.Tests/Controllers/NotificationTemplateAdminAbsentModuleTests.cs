using Tnzi.Notification.Controllers.Admin;
using Tnzi.Template.Dtos;

namespace Tnzi.Notification.Tests.Controllers;

/// <summary>
/// 没加载 <c>Tnzi.Template</c> 时，通知模板管理端的每一个端点答 501。
/// </summary>
/// <remarks>
/// 不是 503：503 是暂时性故障，监控与客户端会一直重试一件在进程生命周期内永远不会好的事。
/// 框架里「可选服务缺席」统一答 501。
/// </remarks>
public class NotificationTemplateAdminAbsentModuleTests
{
    private readonly DefaultNotificationTemplateAdminController _controller = new(templateStoreService: null);

    [Fact]
    public async Task EveryEndpoint_AnswersNotImplemented()
    {
        var codes = new[]
        {
            (await _controller.GetAll(new QueryTemplateRequest())).Code,
            (await _controller.GetById(Guid.NewGuid())).Code,
            (await _controller.Create(new CreateTemplateRequest())).Code,
            (await _controller.Update(Guid.NewGuid(), new UpdateTemplateRequest())).Code,
            (await _controller.Delete(Guid.NewGuid())).Code,
            (await _controller.DeleteBatch([Guid.NewGuid()])).Code
        };

        codes.ShouldAllBe(code => code == 501);
    }
}
