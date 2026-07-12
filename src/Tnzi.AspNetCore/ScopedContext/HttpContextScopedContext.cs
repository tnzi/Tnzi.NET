
namespace Tnzi.AspNetCore.ScopedContext;

/// <summary>
/// 基于 HttpContext 的作用域上下文
/// 使用 HttpContext.Items 作为存储后端
/// </summary>
public class HttpContextScopedContext : Tnzi.ScopedContext.ScopedContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    private const string KeyCurrentUserId = ScopedContextItems.CurrentUserId;
    private const string KeyCurrentUserName = ScopedContextItems.CurrentUserName;
    private const string KeyCurrentUserRoles = ScopedContextItems.CurrentUserRoles;
    private const string KeyAuditOperation = ScopedContextItems.AuditOperation;
    private const string KeyClientIpAddress = ScopedContextItems.ClientIpAddress;
    private const string KeyUserAgent = ScopedContextItems.UserAgent;
    private const string KeyTraceId = ScopedContextItems.TraceId;

    public HttpContextScopedContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private HttpContext? HttpContext => _httpContextAccessor.HttpContext;

    private T? GetHttpItem<T>(string key)
    {
        var httpContext = HttpContext;
        if (httpContext?.Items.TryGetValue(key, out var value) == true)
        {
            return value is T typedValue ? typedValue : default;
        }
        return default;
    }

    private void SetHttpItem<T>(string key, T? value)
    {
        var httpContext = HttpContext;
        if (httpContext != null)
        {
            if (value == null)
                httpContext.Items.Remove(key);
            else
                httpContext.Items[key] = value;
        }
    }

    public override string? CurrentUserId
    {
        get
        {
            var httpContext = HttpContext;
            if (httpContext != null)
            {
                return GetHttpItem<string>(KeyCurrentUserId) ?? base.CurrentUserId;
            }
            // HttpContext 为 null 时，返回 null（不读取基类）
            return null;
        }
        set
        {
            var httpContext = HttpContext;
            if (httpContext != null)
            {
                SetHttpItem(KeyCurrentUserId, value);
                base.CurrentUserId = value;
            }
            // HttpContext 为 null 时，不设置任何值
        }
    }

    public override string? CurrentUserName
    {
        get => GetHttpItem<string>(KeyCurrentUserName) ?? base.CurrentUserName;
        set
        {
            SetHttpItem(KeyCurrentUserName, value);
            base.CurrentUserName = value;
        }
    }

    public override IReadOnlyList<string> CurrentUserRoles
    {
        get => GetHttpItem<IReadOnlyList<string>>(KeyCurrentUserRoles) ?? base.CurrentUserRoles;
        set
        {
            SetHttpItem(KeyCurrentUserRoles, value);
            base.CurrentUserRoles = value;
        }
    }

    public override AuditOperationInfo? AuditOperation
    {
        get => GetHttpItem<AuditOperationInfo>(KeyAuditOperation) ?? base.AuditOperation;
        set
        {
            SetHttpItem(KeyAuditOperation, value);
            base.AuditOperation = value;
        }
    }

    public override string? ClientIpAddress
    {
        get
        {
            var httpContext = HttpContext;
            if (httpContext != null)
            {
                // 优先从 HttpContext.Items 获取（可能由中间件设置）
                var itemValue = GetHttpItem<string>(KeyClientIpAddress);
                if (!string.IsNullOrEmpty(itemValue))
                {
                    return itemValue;
                }
                // 如果 Items 中没有，直接从 HttpContext 获取
                return httpContext.GetClientIp() ?? base.ClientIpAddress;
            }
            return base.ClientIpAddress;
        }
        set
        {
            SetHttpItem(KeyClientIpAddress, value);
            base.ClientIpAddress = value;
        }
    }

    public override string? UserAgent
    {
        get
        {
            var httpContext = HttpContext;
            if (httpContext != null)
            {
                // 优先从 HttpContext.Items 获取（可能由中间件设置）
                var itemValue = GetHttpItem<string>(KeyUserAgent);
                if (!string.IsNullOrEmpty(itemValue))
                {
                    return itemValue;
                }
                // 如果 Items 中没有，直接从 HttpContext 获取
                return httpContext.GetUserAgent() ?? base.UserAgent;
            }
            return base.UserAgent;
        }
        set
        {
            SetHttpItem(KeyUserAgent, value);
            base.UserAgent = value;
        }
    }

    public override string? TraceId
    {
        get => GetHttpItem<string>(KeyTraceId) ?? base.TraceId;
        set
        {
            SetHttpItem(KeyTraceId, value);
            base.TraceId = value;
        }
    }

    public override void SetItem<T>(string key, T value)
    {
        var httpContext = HttpContext;
        if (httpContext != null)
        {
            httpContext.Items[key] = value;
        }
        base.SetItem(key, value);
    }

    // override（不是 new）：基类 GetItem&lt;T&gt; 是 virtual，用 new 会在经 IScopedContext
    // 接口或基类引用调用时被静态派发回基类实现，从而绕过 HttpContext.Items（与同类
    // SetItem 的 override 不对称）。改为 override 后接口调用也走本实现。
    public override T? GetItem<T>(string key) where T : default
    {
        var httpContext = HttpContext;
        if (httpContext?.Items.TryGetValue(key, out var value) == true)
        {
            if (value is T typedValue)
                return typedValue;
        }
        return base.GetItem<T>(key);
    }
}