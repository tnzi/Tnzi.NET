using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Mvc.Filters;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// 全局 <see cref="ModelStateValidationFilter"/> 拒绝请求时，信封的 message 必须点名第一条字段错误。
///
/// 此前它一律回固定的 "Validation failed"，字段错误只进 errorDetails；而同一个包里的
/// <c>ApiControllerBase.ValidationError()</c> 早就把第一条错误原文放进 message。消费方的错误提示
/// 通常只显示 message、不读 errorDetails，于是手工验证的端点给用户看到「The FirstName field is required.」，
/// 靠全局过滤器的端点给用户看到「Validation failed」—— 一个框架，同一个问题，两种回答，
/// 后一种什么都做不了：某消费应用一份「保存时一直报 Validation failed」的缺陷报告，
/// 根因是嵌套集合元素上的 <c>[Required]</c>（界面刻意先填一行空白的 <c>Directory.People[0].FirstName</c>），
/// 排查花了一整段会话，而 errorDetails 里那条消息本可以直接指向那一行。
/// </summary>
/// <remarks>
/// 嵌套 DTO 走真实的 DataAnnotations 验证器（<see cref="IObjectModelValidator"/>），不手工 <c>AddModelError</c>：
/// 要证明的正是「框架自己产出的键与消息」到达信封，手填的键证明不了这一点。
/// </remarks>
public class ModelStateValidationFilterTests
{
    private const string NestedFirstNameKey = "Directory.People[0].FirstName";
    private const string RequiredFirstNameMessage = "The FirstName field is required.";

    private sealed class PersonDto
    {
        [Required]
        public string? FirstName { get; set; }
    }

    private sealed class DirectoryDto
    {
        public List<PersonDto> People { get; set; } = [];
    }

    private sealed class CreateCaseRequest
    {
        [Required]
        public string? Title { get; set; } = "A case";

        public DirectoryDto Directory { get; set; } = new();
    }

    /// <summary>
    /// 把 <c>ValidationError()</c> 露出来：它是 protected，而这里要拿它和过滤器比对。
    /// </summary>
    private sealed class ProbeController : ApiControllerBase
    {
        public ApiResult Probe() => ValidationError();

        public ApiResult<object> ProbeTyped() => ValidationError<object>();
    }

    private static ActionContext BuildActionContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        // ControllerContext 只接受 ControllerActionDescriptor；它是 ActionDescriptor 的子类，过滤器那侧照常用。
        return new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor { EndpointMetadata = new List<object>() });
    }

    /// <summary>
    /// 用 MVC 自己的验证器跑一遍模型：键与消息都由框架产出。
    /// </summary>
    private static ActionContext Validate(object model)
    {
        var actionContext = BuildActionContext();
        var validator = actionContext.HttpContext.RequestServices.GetRequiredService<IObjectModelValidator>();
        validator.Validate(actionContext, validationState: null, prefix: string.Empty, model);
        return actionContext;
    }

    private static async Task<ApiResult<object>> RunFilter(ActionContext actionContext)
    {
        var context = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);

        var reached = false;
        await new ModelStateValidationFilter().OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller));
        });

        Assert.False(reached, "an invalid ModelState must short-circuit the action");
        var result = Assert.IsType<BadRequestObjectResult>(context.Result);
        return Assert.IsType<ApiResult<object>>(result.Value);
    }

    private static Dictionary<string, List<string>> ErrorDetailsOf(ApiResult<object> envelope)
        => Assert.IsType<Dictionary<string, List<string>>>(envelope.ErrorDetails);

    private static void AssertSameErrorDetails(ApiResult<object> expected, ApiResult<object> actual)
    {
        var expectedDetails = ErrorDetailsOf(expected);
        var actualDetails = ErrorDetailsOf(actual);

        Assert.Equal(expectedDetails.Keys.Order(), actualDetails.Keys.Order());
        foreach (var key in expectedDetails.Keys)
        {
            Assert.Equal(expectedDetails[key], actualDetails[key]);
        }
    }

    [Fact]
    public async Task RequiredOnNestedCollectionElement_MessageNamesTheField()
    {
        var actionContext = Validate(new CreateCaseRequest
        {
            Directory = new DirectoryDto { People = [new PersonDto { FirstName = null }] }
        });
        Assert.False(actionContext.ModelState.IsValid);

        var envelope = await RunFilter(actionContext);

        Assert.Equal(400, envelope.Code);
        Assert.False(envelope.Succeeded);
        Assert.Equal(RequiredFirstNameMessage, envelope.Message);

        // errorDetails 一字不变：完整的「字段 → 错误列表」字典，键是嵌套路径。
        var details = ErrorDetailsOf(envelope);
        Assert.True(details.TryGetValue(NestedFirstNameKey, out var fieldErrors), $"errorDetails must carry the key '{NestedFirstNameKey}'");
        Assert.Equal(new[] { RequiredFirstNameMessage }, fieldErrors);
    }

    [Fact]
    public async Task NoReadableError_FallsBackToValidationFailed()
    {
        // 绑定失败只设了 Exception 而消息为空：ModelState 无效，却没有一条能给人看的错误。
        var actionContext = BuildActionContext();
        Assert.True(actionContext.ModelState.TryAddModelException("body", new Exception(string.Empty)));
        Assert.False(actionContext.ModelState.IsValid);

        var envelope = await RunFilter(actionContext);

        Assert.Equal("Validation failed", envelope.Message);
        Assert.Equal(400, envelope.Code);
        Assert.Empty(ErrorDetailsOf(envelope));
    }

    [Fact]
    public async Task EmptyErrorMessageWithException_UsesTheExceptionMessage()
    {
        var actionContext = BuildActionContext();
        actionContext.ModelState.TryAddModelException("amount", new FormatException("The value 'abc' is not valid for Amount."));

        var envelope = await RunFilter(actionContext);

        Assert.Equal("The value 'abc' is not valid for Amount.", envelope.Message);
    }

    [Fact]
    public async Task FirstUnreadableErrorOnAField_DoesNotHideTheReadableOneBehindIt()
    {
        // 同一个字段先记了一条空消息的异常、再记了一条可读错误：errorDetails 里有后者，
        // message 也必须是后者 —— 否则 message 说 "Validation failed" 而 errorDetails 明明写着原因。
        var actionContext = BuildActionContext();
        actionContext.ModelState.TryAddModelException("amount", new Exception(string.Empty));
        actionContext.ModelState.AddModelError("amount", "Amount must be positive.");

        var envelope = await RunFilter(actionContext);

        Assert.Equal("Amount must be positive.", envelope.Message);
        Assert.Equal(new[] { "Amount must be positive." }, ErrorDetailsOf(envelope)["amount"]);
    }

    [Fact]
    public async Task FilterAndValidationError_ComposeTheSameEnvelopeForTheSameModelState()
    {
        var actionContext = Validate(new CreateCaseRequest
        {
            Title = null,
            Directory = new DirectoryDto { People = [new PersonDto(), new PersonDto { FirstName = "Ada" }] }
        });
        var controller = new ProbeController { ControllerContext = new ControllerContext(actionContext) };

        var fromFilter = await RunFilter(actionContext);
        var fromController = controller.Probe();
        var fromTypedController = controller.ProbeTyped();

        Assert.Equal(fromFilter.Message, fromController.Message);
        Assert.Equal(fromFilter.Message, fromTypedController.Message);
        Assert.Equal(fromFilter.Code, fromController.Code);
        AssertSameErrorDetails(fromFilter, fromController);
        AssertSameErrorDetails(fromFilter, fromTypedController);
    }

    [Fact]
    public async Task IgnoreAttribute_LetsAnInvalidModelThrough()
    {
        var actionContext = Validate(new CreateCaseRequest { Title = null });
        actionContext.ActionDescriptor.EndpointMetadata = [new ModelStateValidationAttribute { Ignore = true }];
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: null!);

        var reached = false;
        await new ModelStateValidationFilter().OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller));
        });

        Assert.True(reached);
        Assert.Null(context.Result);
    }
}
