using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Results;
using Tnzi.Signing.Controllers;

namespace Tnzi.Signing.Tests;

/// <summary>
/// <c>GET signing/{token}/document</c> 绝不能把**活动内容**内联交给浏览器。
/// </summary>
/// <remarks>
/// <para>
/// 这个端点是 <c>[AllowAnonymous]</c> 的，而它发出的 <c>Content-Type</c> 来自上传时按<b>上传者给的文件名</b>
/// 算出的 <c>FileRecord.ContentType</c>（<c>.html → text/html</c>）。不带下载文件名的
/// <c>File(bytes, contentType)</c> 没有 <c>Content-Disposition</c>，浏览器按类型渲染 —— 于是一个模板管理员
/// 传一个 <c>payload.html</c> 当渲染稿，发起一份请求，收件人（匿名外部人，或恰好登录着的员工）
/// 打开签署链接，脚本就跑在 API 的源上。
/// </para>
/// <para>
/// 修法与 <c>files/{id}/preview</c> 同形（2026-09-04）：<c>FileTypeHelper.IsInlineRenderable</c> 白名单，
/// 其余一律 <c>attachment</c> + <c>Content-Security-Policy: sandbox</c>，两个分支都加 <c>nosniff</c>。
/// 这里把结果真的交给 MVC 的执行器跑一遍，断言的是响应上**实际写出的**头。
/// </para>
/// </remarks>
public class SigningDocumentInlineSafetyTests
{
    private static readonly byte[] Payload = "<script>alert(document.cookie)</script>"u8.ToArray();

    [Theory]
    [InlineData("payload.html", "text/html")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("blob.bin", "application/octet-stream")]
    public async Task A_non_pdf_rendered_document_is_sent_as_attachment_with_sandbox(string fileName, string contentType)
    {
        var http = await ExecuteGetDocumentAsync(fileName, contentType, download: false);

        var disposition = http.Response.Headers.ContentDisposition.ToString();
        disposition.ShouldStartWith("attachment", Case.Insensitive);
        disposition.ShouldContain(fileName);
        http.Response.ContentType.ShouldStartWith(contentType);
        http.Response.Headers.XContentTypeOptions.ToString().ShouldBe("nosniff");
        http.Response.Headers.ContentSecurityPolicy.ToString().ShouldBe("sandbox");
    }

    [Fact]
    public async Task A_pdf_is_still_inline_with_nosniff()
    {
        var http = await ExecuteGetDocumentAsync("contract.pdf", "application/pdf", download: false);

        http.Response.Headers.ContentDisposition.ToString().ShouldBeEmpty();
        http.Response.ContentType.ShouldStartWith("application/pdf");
        http.Response.Headers.XContentTypeOptions.ToString().ShouldBe("nosniff");
    }

    [Fact]
    public async Task Download_is_an_attachment_for_a_pdf_too()
    {
        var http = await ExecuteGetDocumentAsync("contract.pdf", "application/pdf", download: true);

        var disposition = http.Response.Headers.ContentDisposition.ToString();
        disposition.ShouldStartWith("attachment", Case.Insensitive);
        disposition.ShouldContain("contract.pdf");
        http.Response.Headers.XContentTypeOptions.ToString().ShouldBe("nosniff");
    }

    [Fact]
    public async Task The_bytes_are_still_delivered_when_attached()
    {
        // 附件化改变的是浏览器怎么处置，不是内容本身。
        var http = await ExecuteGetDocumentAsync("payload.html", "text/html", download: false);

        http.Response.Body.Position = 0;
        using var written = new MemoryStream();
        await http.Response.Body.CopyToAsync(written);
        written.ToArray().ShouldBe(Payload);
    }

    // ── 夹具：真控制器 + mock 服务 + MVC 执行器 ──────────────────────────

    private static async Task<HttpContext> ExecuteGetDocumentAsync(string fileName, string contentType, bool download)
    {
        const string token = "token";
        var service = new Mock<IEnvelopeService>();
        service.Setup(s => s.GetDocumentByTokenAsync(token, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SigningDocumentContent(new MemoryStream(Payload), contentType, fileName)));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = provider };
        http.Response.Body = new MemoryStream();

        var controller = new DefaultSigningController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = http,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            }
        };

        var result = await controller.GetDocument(token, download);
        await result.ExecuteResultAsync(controller.ControllerContext);
        return http;
    }
}

/// <summary>
/// 服务层的纵深：这个端点的契约是一份 PDF，渲染稿不是 PDF 时收件人面直接拒绝 ——
/// 控制器可以被消费方整体替换，而服务层是唯一必经处。
/// </summary>
public class SigningDocumentContentTypeTests : SigningStorageTestBase
{
    [Fact]
    public async Task GetDocumentByToken_refuses_a_rendered_document_that_is_not_a_pdf()
    {
        StorageOptions.AllowedExtensions = [".pdf", ".html"];
        var (_, _, token) = await ArrangeSentEnvelopeAsync(renderedFileName: "payload.html");

        using var read = BeginRequest();
        var document = await read.Envelopes.GetDocumentByTokenAsync(token);

        document.Succeeded.ShouldBeFalse();
        document.Code.ShouldBe(404);
    }

    [Fact]
    public async Task GetDocumentByToken_hands_out_a_pdf()
    {
        var (_, _, token) = await ArrangeSentEnvelopeAsync();

        using var read = BeginRequest();
        var document = await read.Envelopes.GetDocumentByTokenAsync(token);

        document.Succeeded.ShouldBeTrue(document.Message);
        document.Data!.ContentType.ShouldBe("application/pdf");
    }
}
