using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Tnzi.Storage.Controllers;
using Tnzi.Storage.Filters;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 上传端点的传输层上限跟着 <see cref="StorageOptions.MaxFileSize"/> 的<b>当前值</b>走（<see cref="StorageUploadLimitAttribute"/>）。
/// </summary>
/// <remarks>
/// <c>MaxFileSize</c> 是设置中心里可以热改的一项，业务闸门读当前值；服务器级上限却是启动时按配置算一次的常量，
/// 而设置中心的值在启动那一刻还不在配置里。管理员把上限调大、界面提示成功，大文件照样在控制器之前被 413 拦下，重启也一样。
/// </remarks>
public class StorageUploadLimitTests
{
    private const long MB = 1024 * 1024;

    private sealed class BodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly { get; init; }
        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }

    private static DefaultHttpContext NewRequest(StorageOptions options, BodySizeFeature feature, Action<FormOptions>? configureForm = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<StorageOptions>>(new StaticOptionsMonitor<StorageOptions>(options));
        services.AddOptions();
        if (configureForm != null)
            services.Configure(configureForm);

        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
        return http;
    }

    private static void RunFilter(HttpContext http)
    {
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>());
        new StorageUploadLimitAttribute().OnAuthorization(context);
    }

    /// <summary>★★ 同一个进程里，设置改了，下一个上传请求的上限就跟着变（变异：退回启动期常量即红）。</summary>
    [Fact]
    public void TheBodyLimit_FollowsTheCurrentMaxFileSize()
    {
        var options = new StorageOptions { MaxFileSize = 100 * MB };

        var first = new BodySizeFeature();
        RunFilter(NewRequest(options, first));
        Assert.Equal(100 * MB + StorageUploadLimitAttribute.Allowance, first.MaxRequestBodySize);

        options.MaxFileSize = 500 * MB;
        var second = new BodySizeFeature();
        RunFilter(NewRequest(options, second));
        Assert.Equal(500 * MB + StorageUploadLimitAttribute.Allowance, second.MaxRequestBodySize);
    }

    /// <summary>请求体已开始读取（特性只读）时什么都不改，退回服务器级上限。</summary>
    [Fact]
    public void AReadOnlyFeature_IsLeftAlone()
    {
        var feature = new BodySizeFeature { IsReadOnly = true };

        RunFilter(NewRequest(new StorageOptions { MaxFileSize = 500 * MB }, feature));

        Assert.Equal(30_000_000, feature.MaxRequestBodySize);
    }

    /// <summary>multipart 正文的上限同样跟着当前值走：超过它的表单读不出来。</summary>
    [Fact]
    public async Task TheMultipartLimit_FollowsTheCurrentMaxFileSize()
    {
        var http = NewRequest(new StorageOptions { MaxFileSize = 1 }, new BodySizeFeature());
        SetMultipartBody(http, fields: 1, fileBytes: (int)(StorageUploadLimitAttribute.Allowance + 4096));

        RunFilter(http);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => http.Request.ReadFormAsync());
        Assert.Contains("Multipart body length limit", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>对照：宿主对表单的其余配置（这里是字段数上限）照旧生效，换掉的只有 multipart 正文上限。</summary>
    [Fact]
    public async Task TheHostsOtherFormOptions_StillApply()
    {
        var http = NewRequest(new StorageOptions { MaxFileSize = 100 * MB }, new BodySizeFeature(), form => form.ValueCountLimit = 1);
        SetMultipartBody(http, fields: 2, fileBytes: 16);

        RunFilter(http);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => http.Request.ReadFormAsync());
        Assert.Contains("value count limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 门禁：默认控制器上每一个收文件的端点都挂着这个特性。新加的上传端点漏挂，就退回启动期的服务器级上限。
    /// </summary>
    [Fact]
    public void EveryFileReceivingEndpoint_CarriesTheLimit()
    {
        var receivers = typeof(DefaultStorageController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Any(p => typeof(IFormFile).IsAssignableFrom(p.ParameterType)
                                                  || typeof(IEnumerable<IFormFile>).IsAssignableFrom(p.ParameterType)))
            .ToList();

        Assert.True(receivers.Count >= 4, $"expected at least 4 upload endpoints, found {receivers.Count}");
        var missing = receivers.Where(m => m.GetCustomAttribute<StorageUploadLimitAttribute>() == null).Select(m => m.Name).ToList();
        Assert.True(missing.Count == 0, "Upload endpoints without [StorageUploadLimit]: " + string.Join(", ", missing));
    }

    private static void SetMultipartBody(HttpContext http, int fields, int fileBytes)
    {
        const string boundary = "----tnzi-test-boundary";
        var body = new MemoryStream();
        void Write(string text) => body.Write(Encoding.UTF8.GetBytes(text));

        for (var i = 0; i < fields; i++)
        {
            Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"field{i}\"\r\n\r\nvalue{i}\r\n");
        }

        Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"big.bin\"\r\nContent-Type: application/octet-stream\r\n\r\n");
        body.Write(new byte[fileBytes]);
        Write($"\r\n--{boundary}--\r\n");

        body.Position = 0;
        http.Request.Method = HttpMethods.Post;
        http.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        http.Request.ContentLength = body.Length;
        http.Request.Body = body;
    }
}
