using Tnzi.AspNetCore.Http;
using Tnzi.Security;

namespace Tnzi.AspNetCore.Tests.Http;

/// <summary>
/// <see cref="HttpContextExtensions.GetRedactedPath"/>：路径里值命中敏感名单的路由参数换成掩码，其余逐字保留。
/// </summary>
public class RedactedPathTests
{
    private const string Token = "Zq3x-SECRET-LINK-TOKEN-0123456789abcdef";

    private static DefaultHttpContext Request(string path, params (string Name, object? Value)[] routeValues)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        foreach (var (name, value) in routeValues)
        {
            context.Request.RouteValues[name] = value;
        }

        return context;
    }

    [Fact]
    public void ATokenSegment_IsMasked()
    {
        var context = Request($"/api/signing/{Token}/document", ("controller", "DefaultSigning"), ("action", "GetDocument"), ("token", Token));

        Assert.Equal($"/api/signing/{QueryStringRedactor.RedactedValue}/document", context.GetRedactedPath());
    }

    [Fact]
    public void OrdinaryRouteValues_AreKept()
    {
        var id = Guid.NewGuid().ToString();
        var context = Request($"/api/orders/{id}", ("controller", "Orders"), ("action", "Get"), ("id", id));

        Assert.Equal($"/api/orders/{id}", context.GetRedactedPath());
    }

    [Fact]
    public void NoRouteValues_ReturnsThePathVerbatim()
    {
        // 没有匹配到端点（404、SPA 回退）时没有路由参数可判，原样返回。
        Assert.Equal($"/api/unknown/{Token}", Request($"/api/unknown/{Token}").GetRedactedPath());
    }

    [Fact]
    public void AParameterThatIsOnlyPartOfASegment_IsStillMasked()
    {
        var context = Request($"/api/files/share/{Token}.pdf", ("token", Token));

        Assert.Equal($"/api/files/share/{QueryStringRedactor.RedactedValue}.pdf", context.GetRedactedPath());
    }

    [Fact]
    public void ACatchAllValueSpanningSegments_IsMasked()
    {
        var context = Request($"/api/vault/a/{Token}/b", ("password", $"a/{Token}/b"));

        Assert.DoesNotContain(Token, context.GetRedactedPath());
        Assert.StartsWith("/api/vault/", context.GetRedactedPath());
    }

    [Fact]
    public void ACallerSuppliedKeyList_ReplacesTheDefaultList()
    {
        var context = Request($"/api/hooks/{Token}/abc", ("hookKey", Token), ("token", "abc"));

        Assert.Equal($"/api/hooks/{QueryStringRedactor.RedactedValue}/abc", context.GetRedactedPath(["hookKey"]));
    }

    [Fact]
    public void TheKeyComparison_IsCaseInsensitive()
    {
        var context = Request($"/api/invitations/{Token}", ("Token", Token));

        Assert.Equal($"/api/invitations/{QueryStringRedactor.RedactedValue}", context.GetRedactedPath());
    }
}
