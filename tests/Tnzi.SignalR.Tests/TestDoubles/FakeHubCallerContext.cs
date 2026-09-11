using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Tnzi.SignalR.Tests.TestDoubles;

/// <summary>
/// 可控的 <see cref="HubCallerContext"/> 替身。
///
/// SignalR 的 Hub 与过滤器全部经 <c>Context</c> 拿身份、请求服务与
/// <c>Abort()</c>，所以这一个替身就是这批测试的全部夹具。
/// </summary>
public sealed class FakeHubCallerContext : HubCallerContext
{
    private readonly CancellationTokenSource _aborted = new();

    public FakeHubCallerContext(
        string connectionId = "conn-1",
        ClaimsPrincipal? user = null,
        IServiceProvider? requestServices = null,
        string? clientIp = null)
    {
        ConnectionId = connectionId;
        User = user;

        var features = new FeatureCollection();
        if (requestServices != null || clientIp != null)
        {
            var http = new DefaultHttpContext();
            if (requestServices != null) http.RequestServices = requestServices;
            if (clientIp != null) http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(clientIp);
            features.Set<IHttpContextFeature>(new HttpContextFeatureStub(http));
        }
        Features = features;
    }

    public override string ConnectionId { get; }

    public override string? UserIdentifier =>
        User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public override ClaimsPrincipal? User { get; }

    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public override IFeatureCollection Features { get; }

    public override CancellationToken ConnectionAborted => _aborted.Token;

    /// <summary>本连接的 <see cref="Abort"/> 被调用过几次。</summary>
    public int AbortCount { get; private set; }

    public override void Abort()
    {
        AbortCount++;
        _aborted.Cancel();
    }

    /// <summary>
    /// <c>Context.GetHttpContext()</c> 找的是
    /// <see cref="IHttpContextFeature"/>（SignalR 传输层那个，
    /// <c>Microsoft.AspNetCore.Http.Connections.Features</c>），不是
    /// <c>Microsoft.AspNetCore.Http.Features</c> 下的同名类型 —— 后者不存在。
    /// </summary>
    private sealed class HttpContextFeatureStub(HttpContext context) : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; } = context;
    }
}
