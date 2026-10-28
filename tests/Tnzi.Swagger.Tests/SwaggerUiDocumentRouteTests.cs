using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;

namespace Tnzi.Swagger.Tests;

/// <summary>
/// 文档 UI 页面里写的文档地址必须是真有人在服务的地址。
/// 两种 UI 都是「页面加载成功、文档列表为空或 404」这种安静的坏法，单看配置对象或注册的服务都看不出来，
/// 所以这里按各自前端脚本的解析规则把页面里的每个地址取一遍。
/// </summary>
public class SwaggerUiDocumentRouteTests
{
    private static readonly Uri Origin = new("http://localhost/");

    [Theory]
    [InlineData("swagger", null)]
    [InlineData("docs", null)]
    [InlineData("docs", "/app")]
    public async Task Scalar_ListsEveryDocument_AndEachSourceUrlServesThatDocument(string routePrefix, string? pathBase)
    {
        await using var app = await SwaggerTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Swagger:UIProvider"] = "Scalar",
            ["Swagger:RoutePrefix"] = routePrefix,
        }, pathBase);
        var client = app.GetTestClient();
        var pagePath = $"{pathBase}/{routePrefix}/";

        // 取两次：文档列表是每次请求配置一遍的，不能越取越多
        await client.GetAsync(pagePath);
        var page = await client.GetAsync(pagePath);
        page.EnsureSuccessStatusCode();
        var sources = ReadScalarSources(await page.Content.ReadAsStringAsync(), pagePath);

        // 默认文档 + admin 分组，与 Swagger UI 下拉框列出的集合相同
        Assert.Equal(2, sources.Count);
        var documents = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var response = await client.GetAsync(source.ResolvedUrl);
            Assert.True(response.IsSuccessStatusCode, $"Scalar source '{source.ResolvedUrl}' returned {(int)response.StatusCode}.");
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.True(document.TryGetProperty("openapi", out _), $"Scalar source '{source.ResolvedUrl}' is not an OpenAPI document.");
            documents[source.Title] = document;
        }

        Assert.True(documents.TryGetValue("Default API", out var defaultDocument), "The default document is not offered.");
        Assert.True(defaultDocument.GetProperty("paths").TryGetProperty("/probe", out _));
        Assert.True(documents.TryGetValue("Admin API", out var adminDocument), "The admin group document is not offered.");
        Assert.True(adminDocument.GetProperty("paths").TryGetProperty("/probe-admin", out _));
    }

    [Fact]
    public async Task Scalar_DefaultDocumentWithCustomName_IsTheSelectedSource()
    {
        await using var app = await SwaggerTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Swagger:UIProvider"] = "Scalar",
            ["Swagger:DefaultDocument:Name"] = "public",
            ["Swagger:DefaultDocument:Title"] = "Public API",
        });
        var client = app.GetTestClient();

        var page = await client.GetAsync("/swagger/");
        page.EnsureSuccessStatusCode();
        var sources = ReadScalarSources(await page.Content.ReadAsStringAsync(), "/swagger/");

        var defaultSource = Assert.Single(sources, s => s.IsDefault);
        Assert.Equal("Public API", defaultSource.Title);
        var response = await client.GetAsync(defaultSource.ResolvedUrl);
        Assert.True(response.IsSuccessStatusCode, $"Scalar source '{defaultSource.ResolvedUrl}' returned {(int)response.StatusCode}.");
    }

    [Theory]
    [InlineData("swagger", null)]
    [InlineData("docs", null)]
    [InlineData("tools/docs", null)]
    [InlineData("docs", "/app")]
    public async Task SwaggerUi_EveryListedDocumentUrl_IsServed(string routePrefix, string? pathBase)
    {
        await using var app = await SwaggerTestHost.StartAsync(new Dictionary<string, string?>
        {
            ["Swagger:RoutePrefix"] = routePrefix,
        }, pathBase);
        var client = app.GetTestClient();

        var script = await client.GetAsync($"{pathBase}/{routePrefix}/index.js");
        script.EnsureSuccessStatusCode();
        var urls = ReadSwaggerUiUrls(await script.Content.ReadAsStringAsync());

        Assert.Equal(2, urls.Count);
        // index.js 把相对地址拼在 index.html 所在目录后面
        var pageUri = new Uri(Origin, $"{pathBase}/{routePrefix}/index.html");
        foreach (var url in urls)
        {
            var response = await client.GetAsync(new Uri(pageUri, url));
            Assert.True(response.IsSuccessStatusCode, $"Swagger UI document '{url}' under '{pathBase}/{routePrefix}' returned {(int)response.StatusCode}.");
        }
    }

    /// <summary>
    /// 按 scalar.aspnetcore.js 的 initialize 解析来源地址：
    /// basePath = 页面 pathname 去掉服务端给的请求路径后缀（即 PathBase），相对地址按 origin + basePath + "/" 解析
    /// </summary>
    private static List<ScalarSource> ReadScalarSources(string html, string pagePathname)
    {
        // initialize('<request path>', <useDynamicBaseServerUrl>, <configuration JSON>, '<modulePath>')
        var match = Regex.Match(html, @"initialize\(\s*'([^']*)',\s*(?:true|false),\s*(\{.*\}),\s*'[^']*'\)", RegexOptions.Singleline);
        Assert.True(match.Success, "Scalar page carries no initialize(...) configuration.");

        var requestPath = Uri.UnescapeDataString(match.Groups[1].Value);
        var basePath = pagePathname.EndsWith(requestPath, StringComparison.Ordinal) ? pagePathname[..^requestPath.Length] : string.Empty;
        var resolveAgainst = new Uri(Origin, basePath + "/");

        var configuration = JsonDocument.Parse(match.Groups[2].Value).RootElement;
        return configuration.GetProperty("sources").EnumerateArray()
            .Select(source => new ScalarSource(
                source.GetProperty("title").GetString()!,
                new Uri(resolveAgainst, source.GetProperty("url").GetString()!),
                source.TryGetProperty("default", out var isDefault) && isDefault.GetBoolean()))
            .ToList();
    }

    /// <summary>
    /// index.js 把配置写成 JS 对象字面量（<c>var configObject = {...};</c>，Swashbuckle 10.3 起；此前是塞进
    /// <c>JSON.parse('...')</c> 的字符串），序列化结果本身是合法 JSON。
    /// </summary>
    private static List<string> ReadSwaggerUiUrls(string script)
    {
        var match = Regex.Match(script, @"var configObject = (\{.*?\});\s*var oauthConfigObject", RegexOptions.Singleline);
        Assert.True(match.Success, "Swagger UI index.js carries no configObject.");
        var configuration = JsonDocument.Parse(match.Groups[1].Value).RootElement;
        return configuration.GetProperty("urls").EnumerateArray()
            .Select(entry => entry.GetProperty("url").GetString()!)
            .ToList();
    }

    private sealed record ScalarSource(string Title, Uri ResolvedUrl, bool IsDefault);
}
