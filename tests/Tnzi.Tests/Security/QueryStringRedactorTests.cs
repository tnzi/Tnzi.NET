using Microsoft.AspNetCore.Http;

namespace Tnzi.Tests.Security;

/// <summary>
/// 查询串脱敏原语：请求日志与审计表共用的那一份名单与算法。
/// </summary>
/// <remarks>
/// 它住在核心是因为有两个互不相识的消费方（<c>Tnzi.AspNetCore</c> 的请求日志、
/// <c>Tnzi.Audit</c> 的操作审计）。各写一份名单的结果是「某个流程比别处多露出一个键」，
/// 而那不报错也不会让测试变红 —— 审计表此前就是这样把 <c>?token=</c> 原样存了下来。
/// </remarks>
public class QueryStringRedactorTests
{
    private static IQueryCollection Parse(string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);
        return context.Request.Query;
    }

    [Theory]
    [InlineData("?password=hunter2", "password")]
    [InlineData("?sig=1.123.abc.def", "sig")]
    [InlineData("?access_token=eyJhbGciOi", "access_token")]
    [InlineData("?token=reset-me-please", "token")]
    [InlineData("?enrollmentToken=enrol-1", "enrollmentToken")]
    public void EveryDefaultKey_IsReplacedWithThePlaceholder(string query, string key)
    {
        var redacted = QueryStringRedactor.Redact(Parse(query), query, QueryStringRedactor.DefaultSensitiveKeys);

        Assert.Equal($"?{key}={QueryStringRedactor.RedactedValue}", redacted);
    }

    [Fact]
    public void TheOtherParameters_SurviveIntact()
    {
        var redacted = QueryStringRedactor.Redact(
            Parse("?fileId=abc&password=hunter2&expiresInSeconds=600"),
            "?fileId=abc&password=hunter2&expiresInSeconds=600",
            QueryStringRedactor.DefaultSensitiveKeys);

        Assert.Equal("?fileId=abc&password=***&expiresInSeconds=600", redacted);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var redacted = QueryStringRedactor.Redact(Parse("?Password=hunter2"), "?Password=hunter2", QueryStringRedactor.DefaultSensitiveKeys);

        Assert.Equal("?Password=***", redacted);
    }

    [Fact]
    public void RepeatedSensitiveValues_CollapseToOnePlaceholder()
    {
        // 值的个数本身也是信息。
        var redacted = QueryStringRedactor.Redact(Parse("?password=a&password=b"), "?password=a&password=b", QueryStringRedactor.DefaultSensitiveKeys);

        Assert.Equal("?password=***", redacted);
    }

    [Fact]
    public void AQueryWithNothingSensitive_IsReturnedAsTheSameInstance()
    {
        const string query = "?pageIndex=1&pageSize=20";

        var redacted = QueryStringRedactor.Redact(Parse(query), query, QueryStringRedactor.DefaultSensitiveKeys);

        Assert.Same(query, redacted);
    }

    [Fact]
    public void AnEmptyQuery_IsLeftAlone()
    {
        Assert.Equal(string.Empty, QueryStringRedactor.Redact(Parse(string.Empty), string.Empty, QueryStringRedactor.DefaultSensitiveKeys));
        Assert.Null(QueryStringRedactor.Redact(Parse(string.Empty), null, QueryStringRedactor.DefaultSensitiveKeys));
    }

    [Fact]
    public void AnEncodedValue_IsReEncoded_NotMangled()
    {
        // 重建而不是正则替换：编码过的值与含 & 的值都要留得住。
        var redacted = QueryStringRedactor.Redact(
            Parse("?name=a%26b%20c&token=t"),
            "?name=a%26b%20c&token=t",
            QueryStringRedactor.DefaultSensitiveKeys);

        Assert.Equal("?name=a%26b%20c&token=***", redacted);
    }

    [Fact]
    public void ACallerSuppliedList_ReplacesTheDefaultOne()
    {
        var redacted = QueryStringRedactor.Redact(
            Parse("?apiKey=secret&password=hunter2"),
            "?apiKey=secret&password=hunter2",
            ["apiKey"]);

        Assert.Equal("?apiKey=***&password=hunter2", redacted);
    }

    [Fact]
    public void IsSensitive_AnswersPerKey()
    {
        Assert.True(QueryStringRedactor.IsSensitive("Access_Token", QueryStringRedactor.DefaultSensitiveKeys));
        Assert.False(QueryStringRedactor.IsSensitive("pageIndex", QueryStringRedactor.DefaultSensitiveKeys));
    }

    [Fact]
    public void TheDefaultList_CoversWhatTheFrameworkItselfPutsInAQueryString()
    {
        // 框架自己会把这些凭据放进查询串：SignalR 的 access_token、签名文件链接的 sig、
        // 分享链接口令 password、邮件里的重置 / 确认链接 token、passkey 注册 enrollmentToken。
        foreach (var key in new[] { "access_token", "sig", "password", "token", "enrollmentToken" })
        {
            Assert.Contains(key, QueryStringRedactor.DefaultSensitiveKeys, StringComparer.OrdinalIgnoreCase);
        }
    }
}
