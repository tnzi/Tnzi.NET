namespace Tnzi.AspNetCore.Tests.Options;

/// <summary>
/// 查询串令牌只在登记过的路径上被接受，按路径段匹配。
/// </summary>
/// <remarks>
/// 匹配宽一点就是把「令牌进 URL」的风险扩到别的端点：<c>/hubsx</c> 不是 <c>/hubs</c> 下的路径，
/// 一个前缀写错（不以 <c>/</c> 开头）的登记也不能变成「匹配一切」。
/// </remarks>
public class QueryStringTokenOptionsTests
{
    [Theory]
    [InlineData("/hubs", true)]
    [InlineData("/hubs/chat", true)]
    [InlineData("/HUBS/chat", true)]
    [InlineData("/hubsx", false)]
    [InlineData("/api/hubs", false)]
    [InlineData("/", false)]
    public void Default_AcceptsOnlyTheHubsSegment(string path, bool expected)
        => Assert.Equal(expected, new QueryStringTokenOptions().AcceptsQueryToken(new PathString(path)));

    [Fact]
    public void RegisteredPath_IsAccepted_WithItsSubPaths()
    {
        var options = new QueryStringTokenOptions();
        options.PathPrefixes.Add("/ws/gateway");

        Assert.True(options.AcceptsQueryToken(new PathString("/ws/gateway")));
        Assert.True(options.AcceptsQueryToken(new PathString("/ws/gateway/v2")));
        Assert.False(options.AcceptsQueryToken(new PathString("/ws/gatewayx")));
        Assert.False(options.AcceptsQueryToken(new PathString("/ws")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ws/gateway")]
    [InlineData("   ")]
    public void MalformedPrefix_MatchesNothing(string prefix)
    {
        var options = new QueryStringTokenOptions();
        options.PathPrefixes.Clear();
        options.PathPrefixes.Add(prefix);

        Assert.False(options.AcceptsQueryToken(new PathString("/ws/gateway")));
        Assert.False(options.AcceptsQueryToken(new PathString("/api/users")));
    }
}
