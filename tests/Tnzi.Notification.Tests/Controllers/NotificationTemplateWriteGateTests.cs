using Tnzi.AspNetCore.Models;
using Tnzi.Notification.Controllers.Admin;
using Tnzi.Security.Authorization;
using Tnzi.Template.Dtos;

namespace Tnzi.Notification.Tests.Controllers;

/// <summary>
/// 写通知模板等于在本进程里执行代码：新建与修改除了本模块的操作码，还要模板模块那一道代码执行权限。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：通知模板是存进共享模板库的 Razor，发送与预览（只要
/// <c>notification.message.view</c>）都会把它编译成 C# 运行，Razor 里能写任意 C#：读文件、拿连接串。
/// 模板模块自己把这件事标成了代码执行（<c>template.template.create</c> / <c>.update</c>），
/// 而通知模板这一层只挂 <c>notification.template.create</c> / <c>.update</c>，看上去是一个改文案的权限。
/// 运营把它授给市场同事，等于授出了进程级的代码执行，而且进程是所有租户共用的。
/// </para>
/// <para>
/// 现在两道门叠加（AND）：代码执行的授权只有模板模块那一个，授权时只需要衡量它。
/// 特性带 <c>Inherited = true</c>，按文档派生控制器覆写这两个方法时门照样在。
/// </para>
/// </remarks>
public class NotificationTemplateWriteGateTests
{
    [Theory]
    [InlineData(nameof(DefaultNotificationTemplateAdminController.Create), "notification.template.create", "template.template.create")]
    [InlineData(nameof(DefaultNotificationTemplateAdminController.Update), "notification.template.update", "template.template.update")]
    public void WritingATemplate_RequiresTheTemplateModulesCodeExecutionGrantToo(string action, string ownCode, string codeExecutionCode)
    {
        var codes = PermissionCodesOn(typeof(DefaultNotificationTemplateAdminController), action);

        codes.ShouldContain(ownCode);
        codes.ShouldContain(codeExecutionCode);
    }

    /// <summary>派生控制器覆写写端点时，两道门都随方法继承下来（MVC 按 inherit: true 收集特性）。</summary>
    [Theory]
    [InlineData(nameof(DefaultNotificationTemplateAdminController.Create), "template.template.create")]
    [InlineData(nameof(DefaultNotificationTemplateAdminController.Update), "template.template.update")]
    public void AnOverridingController_KeepsTheCodeExecutionGate(string action, string codeExecutionCode)
    {
        PermissionCodesOn(typeof(OverridingTemplateController), action).ShouldContain(codeExecutionCode);
    }

    /// <summary>删除与读取不执行任何模板代码，不需要那道门。</summary>
    [Theory]
    [InlineData(nameof(DefaultNotificationTemplateAdminController.Delete))]
    [InlineData(nameof(DefaultNotificationTemplateAdminController.GetAll))]
    public void ReadingOrDeleting_DoesNotRequireTheCodeExecutionGrant(string action)
    {
        PermissionCodesOn(typeof(DefaultNotificationTemplateAdminController), action)
            .ShouldNotContain(code => code.StartsWith("template.template.", StringComparison.Ordinal));
    }

    private static List<string> PermissionCodesOn(Type controller, string action)
    {
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance)!;

        return method.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true)
            .Concat(controller.GetCustomAttributes<ApiAuthorizeAttribute>(inherit: true))
            .Select(a => a.PermissionName)
            .OfType<string>()
            .ToList();
    }

    private sealed class OverridingTemplateController : DefaultNotificationTemplateAdminController
    {
        public override Task<ApiResult<TemplateDto>> Create(CreateTemplateRequest request)
            => base.Create(request);

        public override Task<ApiResult<TemplateDto>> Update(Guid id, UpdateTemplateRequest request)
            => base.Update(id, request);
    }
}
