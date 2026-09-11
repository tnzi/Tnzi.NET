using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Tnzi.Results;
using Tnzi.Storage.Controllers;
using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests;

/// <summary>
/// <c>GET files/{id}/preview</c> 绝不能把**活动内容**内联交给浏览器。
/// </summary>
/// <remarks>
/// <para>
/// 这个端点是 <c>[AllowAnonymous]</c> 的，而它发出的 <c>Content-Type</c> 来自上传时按<b>客户端文件名</b>
/// 算出的 <c>FileRecord.ContentType</c>（<c>.html → text/html</c>、<c>.svg → image/svg+xml</c>）。
/// 不带下载文件名的 <c>File(stream, contentType)</c> 没有 <c>Content-Disposition</c>，浏览器按类型渲染 ——
/// 于是任何已登录用户传一个 <c>payload.html</c>（<c>isPublic=true</c>），再把预览链接发给受害者，
/// 脚本就跑在 API 的源上（与管理端 API 同源，cookie 交付模式下连 cookie 都带着），还被缓存一年。
/// </para>
/// <para>
/// 修法是白名单：位图 / 视频 / 音频 / PDF / 纯文本可以内联，其余一律 <c>attachment</c>（保留声明的类型，
/// 让 <c>&lt;img&gt;</c> 里的 .svg 仍渲染得出来 —— 那是脚本不执行的上下文），并叠 <c>nosniff</c> 与
/// <c>Content-Security-Policy: sandbox</c> 作纵深。这里把结果真的交给 MVC 的执行器跑一遍，
/// 断言的是响应上**实际写出的**头，而不是 <c>FileStreamResult</c> 上的属性。
/// </para>
/// </remarks>
public class PreviewInlineSafetyTests
{
    private static readonly byte[] Payload = "<script>alert(document.cookie)</script>"u8.ToArray();

    // ── 不能内联的类型：必须是附件 ─────────────────────────────────────────

    [Theory]
    [InlineData("payload.html", "text/html")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("feed.xml", "application/xml")]
    [InlineData("app.js", "application/javascript")]
    [InlineData("site.css", "text/css")]
    [InlineData("data.json", "application/json")]
    [InlineData("blob.bin", "application/octet-stream")]
    public async Task ActiveOrUnknownContent_IsServedAsAnAttachment(string fileName, string contentType)
    {
        var (controller, http) = await ExecutePreviewAsync(fileName, contentType);

        var disposition = http.Response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fileName, disposition, StringComparison.Ordinal);

        // 类型保留原值：.svg 在 <img> 里仍要渲染得出来，Content-Disposition 对子资源加载不生效。
        Assert.StartsWith(contentType, http.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("sandbox", http.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.NotNull(controller);
    }

    [Fact]
    public async Task ARecordWithoutAContentType_IsServedAsAnAttachment()
    {
        var (_, http) = await ExecutePreviewAsync("mystery", contentType: null);

        Assert.StartsWith("attachment", http.Response.Headers.ContentDisposition.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("application/octet-stream", http.Response.ContentType, StringComparison.Ordinal);
    }

    // ── 能内联的类型：照常内联，但声明的类型就是最终类型 ─────────────────────

    [Theory]
    [InlineData("photo.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("scan.tiff", "image/tiff")]
    [InlineData("contract.pdf", "application/pdf")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("song.mp3", "audio/mpeg")]
    [InlineData("notes.txt", "text/plain")]
    public async Task DisplayableContent_StaysInline_WithNosniff(string fileName, string contentType)
    {
        var (_, http) = await ExecutePreviewAsync(fileName, contentType);

        Assert.True(string.IsNullOrEmpty(http.Response.Headers.ContentDisposition.ToString()),
            $"{contentType} should render inline, but got Content-Disposition: {http.Response.Headers.ContentDisposition}");
        Assert.StartsWith(contentType, http.Response.ContentType, StringComparison.Ordinal);
        // 纯文本靠 nosniff 才安全：没有它，老浏览器会把 text/plain 嗅探成 HTML。
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
    }

    [Fact]
    public async Task ThePayloadBytes_AreStillDelivered()
    {
        // 附件化改变的是浏览器怎么处置，不是内容本身：下载下来的字节一个都不能少。
        var (_, http) = await ExecutePreviewAsync("payload.html", "text/html");

        http.Response.Body.Position = 0;
        var written = new MemoryStream();
        await http.Response.Body.CopyToAsync(written);
        Assert.Equal(Payload, written.ToArray());
    }

    // ── 白名单本身 ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("image/heic")]
    [InlineData("IMAGE/JPEG")]
    [InlineData("image/png; charset=binary")]
    [InlineData("video/webm")]
    [InlineData("audio/ogg")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    [InlineData("text/plain; charset=utf-8")]
    public void InlineRenderable_AcceptsDisplayableTypes(string contentType)
        => Assert.True(FileTypeHelper.IsInlineRenderable(contentType), $"{contentType} should be inline-renderable");

    [Theory]
    [InlineData("text/html")]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("application/xhtml+xml")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("application/javascript")]
    [InlineData("text/css")]
    [InlineData("application/json")]
    [InlineData("application/zip")]
    [InlineData("application/octet-stream")]
    [InlineData("")]
    [InlineData(null)]
    public void InlineRenderable_RejectsEverythingElse(string? contentType)
        => Assert.False(FileTypeHelper.IsInlineRenderable(contentType), $"{contentType ?? "<null>"} must not be inline-renderable");

    // ── 夹具：真控制器 + mock 存储服务 + MVC 执行器 ──────────────────────────

    private static async Task<(DefaultStorageController Controller, HttpContext Http)> ExecutePreviewAsync(string fileName, string? contentType)
    {
        var id = Guid.NewGuid();
        var record = new FileRecord
        {
            Id = id,
            FileName = "server-generated-key" + Path.GetExtension(fileName),
            OriginalName = fileName,
            Extension = Path.GetExtension(fileName),
            // 属性声明为非空，但存量行可以是 null（控制器里那句 ?? 就是为它写的）；这里刻意灌进去。
            ContentType = contentType!,
            Size = Payload.Length,
            IsPublic = true
        };

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.GetRecordAsync(id)).ReturnsAsync(Result.Success(record));
        storage.Setup(s => s.GetAsync(id)).ReturnsAsync(Result.Success<Stream>(new MemoryStream(Payload)));

        // 结果要真的执行一遍，所以宿主里得有 MVC 的 IActionResultExecutor<FileStreamResult>。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();

        var controller = new DefaultStorageController(storage.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = http,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            }
        };

        var result = await controller.Preview(id);
        await result.ExecuteResultAsync(controller.ControllerContext);
        return (controller, http);
    }
}
