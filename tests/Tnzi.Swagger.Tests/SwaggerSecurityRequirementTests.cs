namespace Tnzi.Swagger.Tests;

/// <summary>
/// 根 <c>security</c> 必须真的点名声明过的方案。
/// 引用解析不到时 Microsoft.OpenApi 照样序列化，只是输出空对象 <c>{}</c>（「不需要任何凭据」），
/// 于是 Swagger UI 点了 Authorize 也不发 Authorization 头，生成的客户端把所有接口当匿名的
/// </summary>
public class SwaggerSecurityRequirementTests
{
    [Theory]
    [InlineData("v1")]
    [InlineData("admin")]
    public async Task JwtAuth_Enabled_SecurityRequirementNamesTheDeclaredScheme(string documentName)
    {
        await using var app = await SwaggerTestHost.StartAsync(new Dictionary<string, string?>());
        var document = await SwaggerTestHost.GetDocumentAsync(app, documentName);

        Assert.True(document.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _));
        var requirement = Assert.Single(document.GetProperty("security").EnumerateArray());
        Assert.True(requirement.TryGetProperty("Bearer", out _), $"Root security requirement is {requirement.GetRawText()}.");
    }

    [Fact]
    public async Task JwtAuth_CustomSchemeName_IsReferencedByTheRequirement()
    {
        await using var app = await SwaggerTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Swagger:JwtAuth:SchemeName"] = "Jwt",
        });
        var document = await SwaggerTestHost.GetDocumentAsync(app, "v1");

        var requirement = Assert.Single(document.GetProperty("security").EnumerateArray());
        Assert.True(requirement.TryGetProperty("Jwt", out _), $"Root security requirement is {requirement.GetRawText()}.");
    }

    [Fact]
    public async Task JwtAuth_Disabled_EmitsNoSecurityRequirement()
    {
        await using var app = await SwaggerTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Swagger:JwtAuth:Enabled"] = "false",
        });
        var document = await SwaggerTestHost.GetDocumentAsync(app, "v1");

        Assert.False(document.TryGetProperty("security", out _));
        Assert.False(document.TryGetProperty("components", out var components) && components.TryGetProperty("securitySchemes", out _));
    }
}
