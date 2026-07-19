using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using Tnzi.AspNetCore.Mvc;
using Tnzi.Security.Authorization;

namespace Tnzi.Audit.Tests.Middleware;

/// <summary>
/// 采集时写/读分类测试（AuditOperationClassifier 经 AuditMiddleware 全链验证）：
/// [AuditRead] > 方法级操作权限码 > admin 面按最具体门归类（.view=读；
/// 非 .view 动作门+写 HTTP 方法=写）> HTTP 方法+伪读启发式；
/// 分类与提取的权限码持久化到 AuditOperation.IsWrite / PermissionName。
/// </summary>
public class AuditMiddlewareClassificationTests
{
    private sealed class StaticOptionsMonitor(AuditOptions value) : IOptionsMonitor<AuditOptions>
    {
        public AuditOptions CurrentValue => value;
        public AuditOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AuditOptions, string?> listener) => null;
    }

    private sealed class CapturingAuditSender : IAuditSender
    {
        public List<AuditOperation> Captured { get; } = [];

        public Task SendAsync(AuditOperation operation)
        {
            Captured.Add(operation);
            return Task.CompletedTask;
        }
    }

    /// <summary>admin 面样本控制器（分类器经 ControllerActionDescriptor 识别）</summary>
    private sealed class FakeStaffAdminController : ApiAdminControllerBase;

    private sealed class FakePublicController : ApiControllerBase;

    private static async Task<AuditOperation> RunAsync(
        string method, string path, object[]? endpointMetadata = null,
        (string Controller, string Action)? route = null)
    {
        var sender = new CapturingAuditSender();
        var middleware = new AuditMiddleware(
            _ => Task.CompletedTask,
            NullLogger<AuditMiddleware>.Instance,
            sender,
            new StaticOptionsMonitor(new AuditOptions()),
            new RequestBodyRedactor());

        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (route.HasValue)
        {
            context.Request.RouteValues["controller"] = route.Value.Controller;
            context.Request.RouteValues["action"] = route.Value.Action;
        }
        if (endpointMetadata != null)
        {
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(endpointMetadata), "test-endpoint"));
        }

        await middleware.InvokeAsync(context, new Mock<ICurrentUser>().Object, new EntityAuditCollector());
        return sender.Captured.ShouldHaveSingleItem();
    }

    private static ControllerActionDescriptor AdminDescriptor() => new()
    {
        ControllerTypeInfo = typeof(FakeStaffAdminController).GetTypeInfo(),
        MethodInfo = typeof(FakeStaffAdminController).GetMethods()[0]
    };

    private static ControllerActionDescriptor PublicDescriptor() => new()
    {
        ControllerTypeInfo = typeof(FakePublicController).GetTypeInfo(),
        MethodInfo = typeof(FakePublicController).GetMethods()[0]
    };

    [Fact]
    public async Task Get_IsRead()
    {
        var operation = await RunAsync("GET", "/api/staff-profiles/1");
        operation.IsWrite.ShouldBe(false);
    }

    [Fact]
    public async Task PlainPost_IsWrite()
    {
        var operation = await RunAsync("POST", "/api/chat/messages");
        operation.IsWrite.ShouldBe(true);
    }

    [Fact]
    public async Task PostQueryPath_IsRead_ByConvention()
    {
        var operation = await RunAsync("POST", "/api/admin/agents/query");
        operation.IsWrite.ShouldBe(false);
    }

    [Fact]
    public async Task PostWithGetActionName_IsRead_ByConvention()
    {
        var operation = await RunAsync("POST", "/api/admin/users/list",
            route: ("UserAdmin", "GetList"));
        operation.IsWrite.ShouldBe(false);
        operation.FunctionName.ShouldBe("UserAdmin.GetList");
    }

    [Fact]
    public async Task AuditReadAttribute_ForcesRead_EvenOnPlainPost()
    {
        var operation = await RunAsync("POST", "/api/admin/staff-profiles/operations-section",
            [new AuditReadAttribute()]);
        operation.IsWrite.ShouldBe(false);
    }

    [Fact]
    public async Task MethodLevelOperationCode_ForcesWrite_AndIsStoredAsPermissionName()
    {
        // 操作码优先于伪读启发式（即便路径以 /query 结尾）
        var operation = await RunAsync("POST", "/api/admin/staff-profiles/query",
        [
            new ApiAuthorizeAttribute { PermissionName = "staff.profile.view" },
            new ApiAuthorizeAttribute { PermissionName = "staff.profile.update" }
        ]);

        operation.IsWrite.ShouldBe(true);
        operation.PermissionName.ShouldBe("staff.profile.update");
    }

    [Fact]
    public async Task AdminEndpoint_WithOnlyViewCode_IsRead()
    {
        // 三层门约定的 admin 面读语义 POST（非 /query 路由、非 .Get 方法名）——
        // 约定下写端点必带方法级操作码，仅类级 .view = 读，零代码自动归类
        var operation = await RunAsync("POST", "/api/admin/staff-profiles/operations-section",
        [
            AdminDescriptor(),
            new ApiAuthorizeAttribute { PermissionName = "staff.profile.view" }
        ]);

        operation.IsWrite.ShouldBe(false);
        operation.PermissionName.ShouldBe("staff.profile.view");
    }

    [Fact]
    public async Task AdminEndpoint_WithCoarseNonViewCode_FallsBackToHttpMethod()
    {
        // 未采用三层门约定的 admin 面（粗粒度自定义码，无类级 .view）：
        // 无法推断"无操作码=读"（写端点可能同样无码），落回启发式 → 普通 POST 判写。
        // 此类应用的伪读 POST 用 [AuditRead] 或 /query、.Get 惯例声明
        var operation = await RunAsync("POST", "/api/admin/fiscal-years/1/close",
        [
            AdminDescriptor(),
            new ApiAuthorizeAttribute { PermissionName = "contoso.finance.manage" }
        ]);

        operation.IsWrite.ShouldBe(true);
        operation.PermissionName.ShouldBe("contoso.finance.manage");
    }

    [Fact]
    public async Task NonAdminEndpoint_WithNonOperationCode_FallsBackToHttpMethod()
    {
        // 用户面 POST 带非 CRUD 授权码（如 chat.use）：不是操作码、不在 admin 面 → 回退方法判定=写
        var operation = await RunAsync("POST", "/api/chat/messages",
        [
            PublicDescriptor(),
            new ApiAuthorizeAttribute { PermissionName = "chat.use" }
        ]);

        operation.IsWrite.ShouldBe(true);
        operation.PermissionName.ShouldBe("chat.use");
    }

    [Fact]
    public async Task AdminEndpoint_WithSemanticActionCode_IsWrite()
    {
        // 三层门 admin 面、标准 CRUD 后缀之外的语义化方法级动作码
        // （authorization.roleFunction.assign、消费应用的 .settle/.close 等）：
        // 类级 .view 之外更具体的门只声明在写端点上 → 写
        var operation = await RunAsync("POST", "/api/admin/role-functions/role/1/assign",
        [
            AdminDescriptor(),
            new ApiAuthorizeAttribute { PermissionName = "authorization.roleFunction.view" },
            new ApiAuthorizeAttribute { PermissionName = "authorization.roleFunction.assign" }
        ]);

        operation.IsWrite.ShouldBe(true);
        operation.PermissionName.ShouldBe("authorization.roleFunction.assign");
    }

    [Fact]
    public async Task AdminGetEndpoint_WithNonViewMethodCode_IsRead()
    {
        // GET 面的非 .view 方法级门（如 finance.eft.download）：动作门信号仅对写 HTTP 方法
        // 生效，GET 恒为读
        var operation = await RunAsync("GET", "/api/admin/finance/eft-batches/1/download",
        [
            AdminDescriptor(),
            new ApiAuthorizeAttribute { PermissionName = "finance.eft.view" },
            new ApiAuthorizeAttribute { PermissionName = "finance.eft.download" }
        ]);

        operation.IsWrite.ShouldBe(false);
        operation.PermissionName.ShouldBe("finance.eft.download");
    }

    [Fact]
    public async Task AdminWriteEndpoint_WithExecuteCode_IsWrite()
    {
        var operation = await RunAsync("POST", "/api/admin/jobs/retry",
        [
            AdminDescriptor(),
            new ApiAuthorizeAttribute { PermissionName = "system.jobs.view" },
            new ApiAuthorizeAttribute { PermissionName = "system.jobs.execute" }
        ]);

        operation.IsWrite.ShouldBe(true);
        operation.PermissionName.ShouldBe("system.jobs.execute");
    }
}
