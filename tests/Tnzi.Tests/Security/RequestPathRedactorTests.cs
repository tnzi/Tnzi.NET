namespace Tnzi.Tests.Security;

/// <summary>
/// 路径里的凭据：请求日志、操作审计、访问日志共用的那一道脱敏。
/// </summary>
/// <remarks>
/// 框架自己就把凭据放在路径里（<c>signing/{token}</c>、<c>files/share/{token}</c>），而三处记录此前都存原始路径：
/// 只持有「看日志」权限的人就能捡到别人的签署链接，以收件人的名义签字。
/// 判定只看路由参数名，所以用例里也有名单上的其它名字（<c>{tempToken}</c>）与合成的路径形状，它们不对应框架的某个端点。
/// </remarks>
public class RequestPathRedactorTests
{
    private const string Secret = "S3CR3T-VALUE-NEVER-LOGGED";

    private static IReadOnlyCollection<string> Keys => QueryStringRedactor.DefaultSensitiveKeys;

    private static KeyValuePair<string, object?>[] Route(params (string Key, object? Value)[] values)
        => values.Select(v => new KeyValuePair<string, object?>(v.Key, v.Value)).ToArray();

    [Theory]
    [InlineData("/api/auth/pending-actions/" + Secret, "tempToken", "/api/auth/pending-actions/***")]
    [InlineData("/api/auth/pending-actions/" + Secret + "/send-email-code", "tempToken", "/api/auth/pending-actions/***/send-email-code")]
    [InlineData("/api/invitations/" + Secret, "token", "/api/invitations/***")]
    [InlineData("/api/signing/" + Secret + "/document", "token", "/api/signing/***/document")]
    [InlineData("/api/files/share/" + Secret + "/download", "token", "/api/files/share/***/download")]
    public void ACredentialRouteValue_IsReplacedInThePath(string path, string key, string expected)
    {
        var redacted = RequestPathRedactor.Redact(path, Route((key, Secret), ("controller", "X"), ("action", "Y")), Keys);

        Assert.Equal(expected, redacted);
    }

    [Fact]
    public void OtherRouteValues_StayInThePath()
    {
        // 脱敏不该让访问日志读不出「哪个用户的哪条会话」。
        const string path = "/api/users/42/sessions/7";

        var redacted = RequestPathRedactor.Redact(path, Route(("userId", "42"), ("sessionId", "7")), Keys);

        Assert.Same(path, redacted);
    }

    [Fact]
    public void TheKeyIsMatchedIgnoringCase()
    {
        var redacted = RequestPathRedactor.Redact("/api/invitations/" + Secret, Route(("Token", Secret)), Keys);

        Assert.Equal("/api/invitations/***", redacted);
    }

    [Fact]
    public void AnEscapedPath_IsMatchedByTheDecodedRouteValue()
    {
        // 审计存的是转义形式（PathString.ToString()），路由值是解码后的。
        var redacted = RequestPathRedactor.Redact("/api/signing/a%2Bb%20c", Route(("token", "a+b c")), Keys);

        Assert.Equal("/api/signing/***", redacted);
    }

    [Fact]
    public void NoRouteValues_OrNothingSensitive_LeavesThePathAlone()
    {
        const string path = "/api/invitations/" + Secret;

        Assert.Same(path, RequestPathRedactor.Redact(path, null, Keys));
        Assert.Same(path, RequestPathRedactor.Redact(path, Route(), Keys));
        Assert.Same(path, RequestPathRedactor.Redact(path, Route(("token", "")), Keys));
        Assert.Null(RequestPathRedactor.Redact(null, Route(("token", Secret)), Keys));
    }

    [Fact]
    public void ACustomKeyList_IsHonoured()
    {
        var redacted = RequestPathRedactor.Redact("/api/hooks/" + Secret, Route(("hookSecret", Secret)), ["hookSecret"]);

        Assert.Equal("/api/hooks/***", redacted);
    }
}
