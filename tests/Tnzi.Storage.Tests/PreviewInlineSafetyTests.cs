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
/// 脚本就跑在 API 的源上（与管理端 API 同源，cookie 交付模式下连 cookie 都带着），还会被缓存。
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
        var (record, http) = await ExecutePreviewAsync(fileName, contentType);

        var disposition = http.Response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fileName, disposition, StringComparison.Ordinal);

        // 类型保留原值：.svg 在 <img> 里仍要渲染得出来，Content-Disposition 对子资源加载不生效。
        Assert.StartsWith(contentType, http.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("sandbox", http.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.NotNull(record);
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

    // ── 缓存指令：public 只给公开文件；私密响应必须每次重验证 ──────────────

    [Fact]
    public async Task Preview_PrivateFile_NeverEmitsPublicCacheControl()
    {
        // 响应体取决于 Authorization / ?sig= / IsPublic。RFC 9111 §3.5：共享缓存对带 Authorization 的
        // 请求默认不存，`public` 恰恰是那个显式的例外 —— 一年的 public 等于把私密合同放进代理与 CDN。
        var (_, http) = await ExecutePreviewAsync("contract.pdf", "application/pdf", isPublic: false);

        var cacheControl = http.Response.Headers.CacheControl.ToString();
        Assert.DoesNotContain("public", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("private", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-cache", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization", http.Response.Headers.Vary.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_PublicFile_EmitsBoundedPublicCacheControl()
    {
        var (_, http) = await ExecutePreviewAsync("photo.png", "image/png", isPublic: true);

        var cacheControl = http.Response.Headers.CacheControl.ToString();
        Assert.Contains("public", cacheControl, StringComparison.OrdinalIgnoreCase);
        // 同一个 id 会随建版本换内容，一年不重验证等于永远看旧字节；上限一小时，靠 ETag 续。
        Assert.Equal("public, max-age=3600", cacheControl);
        Assert.True(string.IsNullOrEmpty(http.Response.Headers.Vary.ToString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Preview_EmitsStrongEtagDerivedFromMd5_NotTheMd5Itself(bool isPublic)
    {
        // FileRecordDto 的契约是 MD5 不对普通读者外露（只有管理端完整性校验单独给），而预览匿名可达；
        // ETag 由 id + MD5 派生：随 MD5 变、同记录同字节恒定、不把原值和去重关系交出去。
        var (record, http) = await ExecutePreviewAsync("photo.png", "image/png", isPublic, md5: "abc123");

        var etag = http.Response.Headers.ETag.ToString();
        Assert.Equal(PreviewEtagHelper.Compute(record), etag);
        Assert.DoesNotContain("abc123", etag);
        Assert.StartsWith("\"", etag);
    }

    [Fact]
    public void PreviewEtag_ChangesWithMd5_AndDiffersBetweenRecordsSharingBytes()
    {
        var a = new FileRecord { Id = Guid.NewGuid(), Md5Hash = "abc123" };
        var b = new FileRecord { Id = Guid.NewGuid(), Md5Hash = "abc123" };
        var aAfterVersion = new FileRecord { Id = a.Id, Md5Hash = "def456" };

        Assert.Equal(PreviewEtagHelper.Compute(a), PreviewEtagHelper.Compute(new FileRecord { Id = a.Id, Md5Hash = "abc123" }));
        Assert.NotEqual(PreviewEtagHelper.Compute(a), PreviewEtagHelper.Compute(aAfterVersion));
        Assert.NotEqual(PreviewEtagHelper.Compute(a), PreviewEtagHelper.Compute(b));
        Assert.Null(PreviewEtagHelper.Compute(new FileRecord { Id = a.Id, Md5Hash = null }));
    }

    [Fact]
    public async Task Preview_WithoutMd5_EmitsNoEtag()
    {
        var (_, http) = await ExecutePreviewAsync("photo.png", "image/png", isPublic: true, md5: null);

        Assert.True(string.IsNullOrEmpty(http.Response.Headers.ETag.ToString()));
    }

    [Theory]
    [InlineData("{etag}")]
    [InlineData("W/{etag}")]
    [InlineData("\"other\", {etag}")]
    [InlineData("*")]
    public async Task Preview_MatchingIfNoneMatch_Returns304_WithoutFetchingTheBytes(string ifNoneMatchTemplate)
    {
        // ETag 由 id 派生，而 id 每次都是新的：先算出这条记录的 ETag 再填进请求头。
        var recordId = Guid.NewGuid();
        var expectedEtag = PreviewEtagHelper.Compute(new FileRecord { Id = recordId, Md5Hash = "abc123" })!;
        var (_, http, storage) = await ExecutePreviewWithStorageAsync(
            "photo.png", "image/png", isPublic: false, md5: "abc123",
            ifNoneMatch: ifNoneMatchTemplate.Replace("{etag}", expectedEtag), recordId: recordId);

        Assert.Equal(StatusCodes.Status304NotModified, http.Response.StatusCode);
        Assert.Equal(expectedEtag, http.Response.Headers.ETag.ToString());
        // 重验证仍然过了授权（GetRecordAsync），但字节一个都没取。
        storage.Verify(s => s.GetRecordAsync(It.IsAny<Guid>()), Times.Once);
        storage.Verify(s => s.GetForPreviewAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Preview_NonMatchingIfNoneMatch_Returns200()
    {
        var (_, http) = await ExecutePreviewAsync("photo.png", "image/png", isPublic: true, md5: "abc123", ifNoneMatch: "\"stale\"");

        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
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

    private static async Task<(FileRecord Record, HttpContext Http)> ExecutePreviewAsync(
        string fileName, string? contentType, bool isPublic = true, string? md5 = null, string? ifNoneMatch = null)
    {
        var (record, http, _) = await ExecutePreviewWithStorageAsync(fileName, contentType, isPublic, md5, ifNoneMatch);
        return (record, http);
    }

    private static async Task<(FileRecord Record, HttpContext Http, Mock<IFileStorageService> Storage)> ExecutePreviewWithStorageAsync(
        string fileName, string? contentType, bool isPublic = true, string? md5 = null, string? ifNoneMatch = null, Guid? recordId = null)
    {
        var id = recordId ?? Guid.NewGuid();
        var record = new FileRecord
        {
            Id = id,
            FileName = "server-generated-key" + Path.GetExtension(fileName),
            OriginalName = fileName,
            Extension = Path.GetExtension(fileName),
            // 属性声明为非空，但存量行可以是 null（控制器里那句 ?? 就是为它写的）；这里刻意灌进去。
            ContentType = contentType!,
            Size = Payload.Length,
            IsPublic = isPublic,
            Md5Hash = md5
        };

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.GetRecordAsync(id)).ReturnsAsync(Result.Success(record));
        storage.Setup(s => s.GetForPreviewAsync(id)).ReturnsAsync(Result.Success<Stream>(new MemoryStream(Payload)));

        // 结果要真的执行一遍，所以宿主里得有 MVC 的 IActionResultExecutor<FileStreamResult>。
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();
        if (ifNoneMatch != null)
        {
            http.Request.Headers.IfNoneMatch = ifNoneMatch;
        }

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
        return (record, http, storage);
    }
}
