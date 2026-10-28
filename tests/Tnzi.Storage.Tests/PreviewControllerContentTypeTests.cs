using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Tnzi.Results;
using Tnzi.Storage.Controllers;

namespace Tnzi.Storage.Tests;

/// <summary>
/// <c>files/preview/{id}/preview</c>（<see cref="DefaultStoragePreviewController.GeneratePreview"/>）声明的类型。
/// </summary>
/// <remarks>
/// 此前按预览种类写死：图片一律 <c>image/png</c>、视频一律 <c>video/mp4</c>、音频一律 <c>audio/mpeg</c>。
/// SVG 被声明成 PNG，浏览器不会把它嗅探回 SVG，<c>&lt;img&gt;</c> 与直接打开都是破图，而 <c>preview-url</c>
/// 恰好把 SVG 指到这里。原样返回的字节现在声明记录自己的类型，内联白名单之外的（SVG）与 <c>files/{id}/preview</c>
/// 同形：附件 + <c>sandbox</c>。
/// </remarks>
public class PreviewControllerContentTypeTests
{
    private static readonly byte[] Payload = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

    [Theory]
    [InlineData("photo.webp", "image/webp", "image")]
    [InlineData("photo.jpg", "image/jpeg", "image")]
    [InlineData("clip.webm", "video/webm", "video")]
    [InlineData("voice.wav", "audio/wav", "audio")]
    public async Task OriginalBytes_DeclareTheRecordsOwnType_Inline(string name, string contentType, string kind)
    {
        var http = await ExecuteAsync(name, contentType, kind);

        Assert.StartsWith(contentType, http.Response.ContentType, StringComparison.Ordinal);
        Assert.True(string.IsNullOrEmpty(http.Response.Headers.ContentDisposition.ToString()));
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
    }

    /// <summary>★★ SVG：保留 image/svg+xml（&lt;img&gt; 才渲染得出来），按附件发出并带 sandbox（当文档打开时脚本不执行）。</summary>
    [Fact]
    public async Task Svg_IsDeclaredAsSvg_AsAnAttachment_InASandbox()
    {
        var http = await ExecuteAsync("logo.svg", "image/svg+xml", "image");

        Assert.StartsWith("image/svg+xml", http.Response.ContentType, StringComparison.Ordinal);
        Assert.StartsWith("attachment", http.Response.Headers.ContentDisposition.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("sandbox", http.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
    }

    /// <summary>记录没有类型时按扩展名推，而不是退成 octet-stream（nosniff 下 &lt;img&gt; 会整个拒掉它）。</summary>
    [Fact]
    public async Task ARecordWithoutAContentType_FallsBackToTheExtension()
    {
        var http = await ExecuteAsync("photo.png", contentType: null, "image");

        Assert.StartsWith("image/png", http.Response.ContentType, StringComparison.Ordinal);
    }

    /// <summary>源码类文本按纯文本展示：.html 在这里是被阅读的文字，不是被执行的文档。</summary>
    [Fact]
    public async Task Text_IsAlwaysPlainText()
    {
        var http = await ExecuteAsync("page.html", "text/html", "text");

        Assert.StartsWith("text/plain", http.Response.ContentType, StringComparison.Ordinal);
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
    }

    [Fact]
    public async Task Pdf_IsPdf()
    {
        var http = await ExecuteAsync("contract.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "pdf");

        Assert.StartsWith("application/pdf", http.Response.ContentType, StringComparison.Ordinal);
    }

    private static async Task<HttpContext> ExecuteAsync(string name, string? contentType, string kind)
    {
        var id = Guid.NewGuid();
        var record = new FileRecord
        {
            Id = id,
            FileName = "server-key" + Path.GetExtension(name),
            OriginalName = name,
            Extension = Path.GetExtension(name),
            ContentType = contentType!,
            Path = "2026/10/11/server-key" + Path.GetExtension(name),
        };

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.GetRecordAsync(id)).ReturnsAsync(Result.Success(record));

        var preview = new Mock<IFilePreviewService>();
        preview.Setup(p => p.CanPreview(record)).Returns(true);
        preview.Setup(p => p.GetPreviewType(record)).Returns(kind);
        preview.Setup(p => p.GeneratePreviewAsync(record)).ReturnsAsync(() => new MemoryStream(Payload));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Response.Body = new MemoryStream();

        var controller = new DefaultStoragePreviewController(preview.Object, storage.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = http,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            }
        };

        var result = await controller.GeneratePreview(id);
        await result.ExecuteResultAsync(controller.ControllerContext);
        return http;
    }
}
