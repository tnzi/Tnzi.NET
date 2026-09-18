using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.ModelBinders;

/// <summary>
/// <c>StringTrimModelBinder</c> 的接管范围：它只从值提供者（路由 / 查询串 / 表单）取值，
/// 所以也只能接管这三个来源的字符串。此前它排在 provider 列表第 0 位且不看绑定来源，
/// 于是 <c>[FromBody] string</c> / <c>[FromHeader] string</c> / <c>[ModelBinder(typeof(X))] string</c>
/// 全部被它抢走再交不出值 —— 请求体从不被读、请求头从不被看，而编译期与启动期零提示。
/// </summary>
/// <remarks>
/// 刻意跑真管线而不是单测 <c>GetBinder</c>：缺陷的本体是「provider 顺序 + first-match-wins」，
/// 单测 provider 证不了它在最终列表里排在 <c>BodyModelBinderProvider</c> 之后（或者会让路）。
/// 「修剪仍然生效」那两条是防锈用例：否则「修好了」与「把 provider 整个删了」在断言上长得一样。
/// </remarks>
public class StringTrimModelBinderTests
{
    [Fact]
    public async Task FromBodyString_IsDeserializedFromJsonBody()
    {
        await RunAsync(async client =>
        {
            var response = await client.PostAsync(
                "/api/e2e/string-trim/body",
                new StringContent("\"  hi \"", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            // 请求体由 JSON 输入格式化器反序列化，原样交给 action：不修剪是刻意的。
            Assert.Equal("[  hi ]", await ReadDataAsync(response));
        });
    }

    [Fact]
    public async Task FromHeaderString_IsBoundFromHeader()
    {
        await RunAsync(async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/e2e/string-trim/header");
            request.Headers.Add("X-Trim-Probe", "header-value");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("[header-value]", await ReadDataAsync(response));
        });
    }

    [Fact]
    public async Task ModelBinderAttributeString_ReachesCustomBinder()
    {
        await RunAsync(async client =>
        {
            var response = await client.GetAsync("/api/e2e/string-trim/custom");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("[" + UpperCasingProbeBinder.Marker + "]", await ReadDataAsync(response));
        });
    }

    [Fact]
    public async Task FromQueryString_IsTrimmed()
    {
        await RunAsync(async client =>
        {
            var response = await client.GetAsync("/api/e2e/string-trim/query?q=%20%20a%20");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("[a]", await ReadDataAsync(response));
        });
    }

    [Fact]
    public async Task RouteString_IsTrimmed()
    {
        await RunAsync(async client =>
        {
            var response = await client.GetAsync("/api/e2e/string-trim/route/%20b%20");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("[b]", await ReadDataAsync(response));
        });
    }

    [Fact]
    public async Task FormString_IsTrimmed()
    {
        await RunAsync(async client =>
        {
            var response = await client.PostAsync(
                "/api/e2e/string-trim/form",
                new FormUrlEncodedContent([new KeyValuePair<string, string>("f", "  c  ")]));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("[c]", await ReadDataAsync(response));
        });
    }

    private static async Task<string> ReadDataAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetString() ?? "<null>";
    }

    private static async Task RunAsync(Func<HttpClient, Task> assertAsync)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false"
        });

        var app = await TnziApp.CreateAsync<StringTrimStartupModule>(builder);
        await app.StartAsync();
        try
        {
            await assertAsync(app.GetTestClient());
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

/// <summary>只依赖 AspNetCore 的最小启动模块：模型绑定不需要业务模块。</summary>
[DependsOn(typeof(AspNetCoreModule))]
public sealed class StringTrimStartupModule : TnziCustomModule
{
}

/// <summary>
/// 探针 binder：证明 <c>[ModelBinder(typeof(...))]</c> 指定的 binder 真的被调用了。
/// 它不读任何输入，只写一个固定标记 —— 于是「被 StringTrim 抢走」表现为 null 而不是标记。
/// </summary>
public sealed class UpperCasingProbeBinder : IModelBinder
{
    public const string Marker = "CUSTOM-BINDER-RAN";

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        bindingContext.Result = ModelBindingResult.Success(Marker);
        return Task.CompletedTask;
    }
}

[ApiController]
[Route("e2e/string-trim")]
public sealed class StringTrimProbeController : ApiControllerBase
{
    // 方括号把 null / 空串 / 带空白三种情况在断言里区分开。
    private static string Show(string? value) => value == null ? "<null>" : "[" + value + "]";

    [HttpPost("body")]
    public ApiResult<string> Body([FromBody] string? value) => Ok(Show(value), "Success");

    [HttpGet("header")]
    public ApiResult<string> Header([FromHeader(Name = "X-Trim-Probe")] string? value) => Ok(Show(value), "Success");

    [HttpGet("custom")]
    public ApiResult<string> Custom([ModelBinder(typeof(UpperCasingProbeBinder))] string? value) => Ok(Show(value), "Success");

    [HttpGet("query")]
    public ApiResult<string> Query([FromQuery] string? q) => Ok(Show(q), "Success");

    [HttpGet("route/{segment}")]
    public ApiResult<string> RouteSegment([FromRoute] string? segment) => Ok(Show(segment), "Success");

    [HttpPost("form")]
    public ApiResult<string> Form([FromForm] string? f) => Ok(Show(f), "Success");
}
