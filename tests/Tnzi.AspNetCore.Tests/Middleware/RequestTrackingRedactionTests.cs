namespace Tnzi.AspNetCore.Tests.Middleware;

/// <summary>
/// 请求日志里的查询串脱敏。
///
/// 守的是一条实测踩到的线：本中间件**原样**记录查询串，而查询串里偶尔就是凭据 ——
/// 分享链接口令 `?password=`、文件签名令牌 `?sig=`、SignalR 的 `?access_token=`。
/// 实测确认过：不脱敏时 `?password=hunter2` 会明文写进请求日志，运维随手就能读到。
/// 框架此前只能靠整条路径排除（`/hubs/*` 正是为此），代价是那条路径的日志全丢。
/// </summary>
public class RequestTrackingRedactionTests
{
    /// <summary>反射调用私有的脱敏方法：它是中间件内部实现，不该为测试挪到公开面上。</summary>
    private static string? Redact(string queryString, RequestTrackingOptions? options = null)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);

        var method = typeof(RequestTrackingMiddleware)
            .GetMethod("RedactQueryString", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string?)method.Invoke(null, [context.Request.Query, queryString, options ?? new RequestTrackingOptions()]);
    }

    [Theory]
    [InlineData("?password=hunter2", "password")]
    [InlineData("?sig=1.123.abc.def", "sig")]
    [InlineData("?access_token=eyJhbGciOi", "access_token")]
    public void ACredentialInTheQueryString_IsReplacedWithAPlaceholder(string query, string key)
    {
        var redacted = Redact(query);

        Assert.Contains($"{key}=***", redacted);
        Assert.DoesNotContain("hunter2", redacted);
        Assert.DoesNotContain("1.123.abc.def", redacted);
        Assert.DoesNotContain("eyJhbGciOi", redacted);
    }

    [Fact]
    public void TheOtherParameters_SurviveIntact()
    {
        // 脱敏不该把日志变成一团 *** —— 请求照常留痕，只是凭据的值不见了。
        var redacted = Redact("?fileId=abc&password=hunter2&expiresInSeconds=600");

        Assert.Contains("fileId=abc", redacted);
        Assert.Contains("expiresInSeconds=600", redacted);
        Assert.Contains("password=***", redacted);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        // 客户端大小写不由我们决定，`?Password=` 同样得挡住。
        Assert.Contains("Password=***", Redact("?Password=hunter2"));
    }

    [Fact]
    public void RepeatedSensitiveValues_CollapseToOnePlaceholder()
    {
        // 值的个数本身也是信息。
        var redacted = Redact("?password=a&password=b");

        Assert.Equal("?password=***", redacted);
    }

    [Fact]
    public void AQueryWithNothingSensitive_IsReturnedUntouched()
    {
        const string query = "?pageIndex=1&pageSize=20";

        Assert.Equal(query, Redact(query));
    }

    [Fact]
    public void AnEmptyQuery_IsLeftAlone()
    {
        Assert.Equal(string.Empty, Redact(string.Empty));
    }

    [Fact]
    public void ADeploymentCanSupplyItsOwnKeyList()
    {
        var options = new RequestTrackingOptions { SensitiveQueryKeys = ["apiKey"] };

        var redacted = Redact("?apiKey=secret&password=hunter2", options);

        Assert.Contains("apiKey=***", redacted);
        // 自定义名单**替换**默认名单，不是叠加 —— 部署说了算。
        Assert.Contains("password=hunter2", redacted);
    }
}
