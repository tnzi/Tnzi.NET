using Tnzi.AspNetCore.Http;

namespace Tnzi.AspNetCore.Tests.Http;

/// <summary>
/// `[AjaxOnly]` 的判定来源。
///
/// 守的是这个特性存在的**唯一**理由：跨源的普通导航设不了自定义头。
/// 一个 `&lt;a href&gt;`、一次表单提交、一个 `&lt;img src&gt;` 都带不上 `X-Requested-With`，
/// 而带自定义头的跨源请求会先走预检。把同名**查询参数**也算数，
/// 就等于把这层保护交还给攻击者：一条 `?X-Requested-With=XMLHttpRequest` 的链接
/// 就绕过去了，而这个特性看起来仍然是生效的。
/// </summary>
public class AjaxOnlyTests
{
    private static HttpRequest RequestWith(string? header = null, string? query = null)
    {
        var context = new DefaultHttpContext();
        if (header != null)
        {
            context.Request.Headers["X-Requested-With"] = header;
        }

        if (query != null)
        {
            context.Request.QueryString = new QueryString($"?X-Requested-With={Uri.EscapeDataString(query)}");
        }

        return context.Request;
    }

    [Fact]
    public void TheHeaderCounts()
    {
        Assert.True(RequestWith(header: "XMLHttpRequest").IsAjaxRequest());
    }

    [Fact]
    public void TheQueryParameterDoesNot()
    {
        // 这一条就是整个修复。
        Assert.False(RequestWith(query: "XMLHttpRequest").IsAjaxRequest());
    }

    [Fact]
    public void AQueryParameterCannotStandInForAMissingHeader()
    {
        // 攻击者能控制链接里的查询串，控制不了跨源请求的自定义头。
        var request = RequestWith(header: "fetch", query: "XMLHttpRequest");

        Assert.False(request.IsAjaxRequest());
    }

    [Fact]
    public void APlainNavigationIsNotAjax()
    {
        Assert.False(RequestWith().IsAjaxRequest());
    }

    [Fact]
    public void TheHeaderComparisonIsCaseSensitive()
    {
        // 既有行为，原样保留：客户端库发的是这个确切拼写。
        Assert.False(RequestWith(header: "xmlhttprequest").IsAjaxRequest());
    }
}
