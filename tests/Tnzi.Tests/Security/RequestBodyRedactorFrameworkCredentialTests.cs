namespace Tnzi.Tests.Security;

/// <summary>
/// 默认脱敏名单必须盖住框架<b>自己</b>发出的凭据。
/// </summary>
/// <remarks>
/// 名单按字段名精确匹配，而框架的几个端点用的名字都不在里面：TOTP 绑定的响应是 <c>sharedKey</c> /
/// <c>authenticatorUri</c>（拿到就能生成该用户的第二因子）、MCP 服务器注册的请求带 <c>authToken</c>、
/// 匿名推送注册的响应带 <c>deviceKey</c>（唯一一份明文）、支付响应带 <c>clientSecret</c>。
/// 配置中心的保存体以配置路径为键（<c>Stripe:SecretKey</c>），精确匹配永远对不上。
/// 请求日志与审计的体采集都是配置中心一点就开的开关，开了之后这些值原样进日志与审计表。
/// </remarks>
public class RequestBodyRedactorFrameworkCredentialTests
{
    private const string Secret = "S3CR3T-NEVER-LOGGED";

    private readonly RequestBodyRedactor _redactor = new();

    private string RedactWithDefaults(string json) => _redactor.Redact(json, RequestBodyRedactor.DefaultSensitiveFields);

    [Theory]
    [InlineData("sharedKey")]
    [InlineData("authenticatorUri")]
    [InlineData("authToken")]
    [InlineData("deviceKey")]
    [InlineData("clientSecret")]
    [InlineData("privateKey")]
    [InlineData("secretKey")]
    [InlineData("setPasswordToken")]
    [InlineData("shareToken")]
    [InlineData("passToken")]
    public void FrameworkCredentialField_IsRedacted_AtTopLevelAndNested(string field)
    {
        var top = RedactWithDefaults($$"""{"{{field}}":"{{Secret}}","name":"visible"}""");
        var nested = RedactWithDefaults($$"""{"data":{"items":[{"{{field}}":"{{Secret}}"}]},"name":"visible"}""");

        Assert.DoesNotContain(Secret, top);
        Assert.DoesNotContain(Secret, nested);
        Assert.Contains("visible", top);
        Assert.Contains("visible", nested);
    }

    [Fact]
    public void ConfigurationPathKey_IsJudgedByItsLastSegment()
    {
        var json = $$"""{"Stripe:SecretKey":"{{Secret}}","Identity:Jwt:SecretKey":"{{Secret}}","Mail:Smtp:Password":"{{Secret}}","App:Name":"Shop"}""";

        var result = RedactWithDefaults(json);

        Assert.DoesNotContain(Secret, result);
        Assert.Contains("\"App:Name\":\"Shop\"", result);
    }

    [Fact]
    public void CommonBusinessFieldNames_AreLeftAlone()
    {
        // 这份名单也给实体级审计按属性名跨实体掩码：通用名进来会把全仓的业务编码一起掩掉
        var json = """{"code":"ACC-100","value":"42","name":"n","key":"k","description":"d","Section:Code":"X1"}""";

        Assert.Equal(json, RedactWithDefaults(json));
    }

    [Theory]
    [InlineData("/api/users/profile/two-factor/totp/setup")]
    [InlineData("/api/users/profile/two-factor/enable")]
    [InlineData("/api/admin/users/3f2c/two-factor/disable")]
    [InlineData("/api/admin/settings/payment/stripe-key/encrypted")]
    [InlineData("/API/ADMIN/SETTINGS/G/K/ENCRYPTED/")]
    public void SecretMaterialEndpoints_AreRecognised(string path)
    {
        Assert.True(RequestBodyRedactor.IsSecretBodyPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/api/admin/settings/payment/stripe-key")]
    [InlineData("/api/products")]
    [InlineData("/api/admin/encryption-report")]
    [InlineData("/api/two-factorial")]
    public void OrdinaryEndpoints_AreNotSecretMaterial(string? path)
    {
        Assert.False(RequestBodyRedactor.IsSecretBodyPath(path));
    }
}
