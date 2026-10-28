using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Tnzi.Results;
using Tnzi.Storage.Controllers;

namespace Tnzi.Storage.Tests;

/// <summary>
/// 历史版本下载发出的文件名：版本号插在扩展名之前，下载下来的文件仍能按类型打开。
/// </summary>
/// <remarks>
/// 结果交给 MVC 的 <c>FileStreamResult</c> 执行器真的跑一遍，断言实际写出的 <c>Content-Disposition</c>：
/// 断言 <c>FileDownloadName</c> 属性证明不了响应头里发出去的是什么。
/// </remarks>
public class VersionDownloadFileNameTests
{
    [Theory]
    [InlineData("report.pdf", 2, "report.v2.pdf")]
    [InlineData("archive.tar.gz", 3, "archive.tar.v3.gz")]
    [InlineData("notes", 4, "notes.v4")]
    [InlineData(".gitignore", 5, ".gitignore.v5")]
    [InlineData("notes.", 6, "notes..v6")]
    public async Task DownloadVersion_KeepsTheExtensionLast(string originalName, int version, string expected)
    {
        var disposition = await ExecuteDownloadVersionAsync(originalName, version);

        Assert.StartsWith("attachment", disposition, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"filename={expected}", disposition, StringComparison.Ordinal);
    }

    /// <summary>
    /// 分隔符相关的情形直接测规则本身（带 <c>\</c> 的名字进响应头时会被转义，断言头里的字面量证明不了规则）。
    /// 与前端 <c>versionedFileName</c> 的用例逐条相同：<c>Path.GetExtension</c> 在 Linux 上不认 <c>\</c>，
    /// 用它的话同一个名字在两种服务器上会得出不同结果，与前端也对不上。退回 <c>Path.GetExtension</c> 时，
    /// 在 Linux 上是 <c>a.b\c</c> 一条变红，在 Windows 上是 <c>conf/.env</c>（分隔符后面的点文件）一条变红。
    /// </summary>
    [Theory]
    [InlineData("folder.d/readme", 7, "folder.d/readme.v7")]
    [InlineData("dir\\report.pdf", 8, "dir\\report.v8.pdf")]
    [InlineData("a.b\\c", 9, "a.b\\c.v9")]
    [InlineData("conf/.env", 10, "conf/.env.v10")]
    // ':' 在两边都不是分隔符。
    [InlineData("a.b:c", 11, "a.v11.b:c")]
    public void VersionedFileName_TreatsBothSlashesAsSeparatorsOnEveryOs(string originalName, int version, string expected)
        => Assert.Equal(expected, NameRule.Of(originalName, version));

    /// <summary>只为够到受保护的静态规则；从不实例化。</summary>
    private sealed class NameRule : DefaultStorageController
    {
        private NameRule() : base(null!)
        {
        }

        public static string Of(string fileName, int version) => VersionedFileName(fileName, version);
    }

    private static async Task<string> ExecuteDownloadVersionAsync(string originalName, int version)
    {
        var id = Guid.NewGuid();
        var record = new FileRecord
        {
            Id = id,
            FileName = "server-generated-key" + Path.GetExtension(originalName),
            OriginalName = originalName,
            Extension = Path.GetExtension(originalName),
            ContentType = "application/octet-stream"
        };

        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.GetRecordAsync(id)).ReturnsAsync(Result.Success(record));
        var versions = new Mock<IFileVersionService>();
        versions.Setup(v => v.GetVersionContentAsync(id, version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<Stream>(new MemoryStream("old bytes"u8.ToArray())));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Response.Body = new MemoryStream();

        var controller = new DefaultStorageController(storage.Object, fileVersionService: versions.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = http,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor()
            }
        };

        var result = await controller.DownloadVersion(id, version);
        await result.ExecuteResultAsync(controller.ControllerContext);
        return http.Response.Headers.ContentDisposition.ToString();
    }
}
