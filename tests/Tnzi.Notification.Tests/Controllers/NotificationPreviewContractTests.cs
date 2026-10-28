using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Tnzi.Notification.Controllers.Admin;

namespace Tnzi.Notification.Tests.Controllers;

/// <summary>
/// 预览渲染一份模板，不需要收件人：管理端模板页按模板名 + 变量预览，没有任何地址可填。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：<c>POST admin/notifications/preview</c> 的请求体曾经就是创建通知的
/// <c>CreateNotificationRequest</c>，收件人 <c>[Required] [MinLength(1)]</c>，服务端再校验一遍「至少一个收件人」。
/// 模板页发的是 <c>recipients: []</c>，于是每一次预览都是 400，页面上显示的是读 <c>undefined.subject</c> 的 TypeError，
/// 预览从来没有工作过。这里用 MVC 自己的模型验证器（含非空引用类型的隐式必填）跑一遍真实的请求体，
/// 证明的是「这个端点收得下模板页发出的那份 JSON」，而不是服务方法收得下某个手工构造的对象。
/// </remarks>
public class NotificationPreviewContractTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>模板页预览时发出的请求体：模板名、渠道、变量，没有收件人，也没有标题与正文。</summary>
    private const string TemplatePageBody = """{"type":"Email","templateName":"welcome","templateVariables":{"name":"World"}}""";

    /// <summary>旧客户端照旧带着空的收件人数组。</summary>
    private const string EmptyRecipientsBody = """{"type":"Email","templateName":"welcome","recipients":[],"templateVariables":{"name":"World"}}""";

    [Theory]
    [InlineData(TemplatePageBody)]
    [InlineData(EmptyRecipientsBody)]
    public void PreviewBody_WithoutRecipients_PassesModelValidation(string json)
    {
        var body = JsonSerializer.Deserialize(json, PreviewBodyType(), WireOptions)!;

        var errors = Validate(body);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void PreviewBody_CarriesTheRenderingInputs()
    {
        var body = JsonSerializer.Deserialize(TemplatePageBody, PreviewBodyType(), WireOptions)!;

        var type = body.GetType();
        type.GetProperty("TemplateName")!.GetValue(body).ShouldBe("welcome");
        type.GetProperty("Type")!.GetValue(body).ShouldBe(NotificationType.Email);
    }

    private static Type PreviewBodyType()
    {
        var action = typeof(DefaultNotificationAdminController).GetMethod(nameof(DefaultNotificationAdminController.Preview))!;
        return action.GetParameters().Single(p => p.GetCustomAttribute<FromBodyAttribute>() != null).ParameterType;
    }

    private static List<string> Validate(object model)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        var provider = services.BuildServiceProvider();

        var actionContext = new ActionContext(
            new DefaultHttpContext { RequestServices = provider },
            new RouteData(),
            new ControllerActionDescriptor { EndpointMetadata = new List<object>() });
        provider.GetRequiredService<IObjectModelValidator>()
            .Validate(actionContext, validationState: null, prefix: string.Empty, model);

        return actionContext.ModelState
            .Where(e => e.Value!.Errors.Count > 0)
            .Select(e => $"{e.Key}: {string.Join("; ", e.Value!.Errors.Select(x => x.ErrorMessage))}")
            .ToList();
    }
}
