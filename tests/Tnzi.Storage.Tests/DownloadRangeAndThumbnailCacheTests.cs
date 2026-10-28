using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Tnzi.Results;
using Tnzi.Storage.Controllers;
using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Tests;

/// <summary>
/// <c>files/{id}/download</c> 的 Range 分支与 <c>files/{id}/thumbnail</c> 的缓存头，经 MVC 的执行器真跑一遍。
/// </summary>
/// <remarks>
/// 断言的是响应上实际写出的状态、头与字节，而不是 <c>FileStreamResult</c> 上的属性：Range 那个缺陷只在执行器
/// 拿到一个可 seek 的段流、按请求的 Range 头再切一次时才出现。
/// </remarks>
public class DownloadRangeAndThumbnailCacheTests
{
    private const int FileSize = 1000;
    private static readonly byte[] Content = Enumerable.Range(0, FileSize).Select(i => (byte)(i % 251)).ToArray();

    // ── Range 下载 ───────────────────────────────────────────────────────

    /// <summary>
    /// ★★ provider 已经截好 [start, end] 这一段、控制器已经手写 206 与 Content-Range。段流可 seek 时（内置 InMemory），
    /// 执行器此前把这一段当成整个文件再切一次：中段 416、首段总长被改写成段长、跨段发出错位的字节。
    /// </summary>
    [Theory]
    [InlineData("bytes=100-199", 100, 199)]
    [InlineData("bytes=0-99", 0, 99)]
    [InlineData("bytes=50-149", 50, 149)]
    [InlineData("bytes=900-", 900, FileSize - 1)]
    public async Task ARangeRequest_ServesExactlyThatSlice_WithTheWholeFileLength(string range, long start, long end)
    {
        var (http, _) = await ExecuteDownloadAsync(range);

        Assert.Equal(StatusCodes.Status206PartialContent, http.Response.StatusCode);
        Assert.Equal($"bytes {start}-{end}/{FileSize}", http.Response.Headers.ContentRange.ToString());
        Assert.Equal(end - start + 1, http.Response.ContentLength);
        Assert.Equal(Content[(int)start..((int)end + 1)], ReadBody(http));
    }

    // ── 缩略图缓存头 ─────────────────────────────────────────────────────

    [Fact]
    public async Task APrivateThumbnail_MustRevalidate_AndIsNeverPublic()
    {
        var (http, _, record) = await ExecuteThumbnailAsync(isPublic: false);

        var cacheControl = http.Response.Headers.CacheControl.ToString();
        Assert.Equal("private, no-cache", cacheControl);
        Assert.Contains("Authorization", http.Response.Headers.Vary.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PreviewEtagHelper.ComputeThumbnail(record), http.Response.Headers.ETag.ToString());
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
    }

    [Fact]
    public async Task APublicThumbnail_IsCachedForABoundedTime()
    {
        var (http, _, _) = await ExecuteThumbnailAsync(isPublic: true);

        Assert.Equal("public, max-age=3600", http.Response.Headers.CacheControl.ToString());
        Assert.False(string.IsNullOrEmpty(http.Response.Headers.ETag.ToString()));
    }

    /// <summary>★★ 重验证仍过授权（取记录），但命中时一个字节都不取：不下载 provider 对象、不发访问事件。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMatchingIfNoneMatch_Returns304_WithoutFetchingTheThumbnail(bool isPublic)
    {
        var recordId = Guid.NewGuid();
        var etag = PreviewEtagHelper.ComputeThumbnail(Record(recordId, isPublic))!;

        var (http, storage, _) = await ExecuteThumbnailAsync(isPublic, ifNoneMatch: etag, recordId: recordId);

        Assert.Equal(StatusCodes.Status304NotModified, http.Response.StatusCode);
        Assert.Equal(etag, http.Response.Headers.ETag.ToString());
        storage.Verify(s => s.GetRecordAsync(recordId), Times.Once);
        storage.Verify(s => s.GetThumbnailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AStaleIfNoneMatch_ServesTheThumbnail()
    {
        var (http, _, _) = await ExecuteThumbnailAsync(isPublic: true, ifNoneMatch: "\"stale\"");

        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        Assert.Equal(Content, ReadBody(http));
    }

    /// <summary>缩略图换了键（建版本、回填）就换 ETag；且与同一条记录预览的 ETag 不是同一个值，两条路由的缓存互不冒用。</summary>
    [Fact]
    public void TheThumbnailEtag_FollowsTheThumbnailKey_AndDiffersFromThePreviewEtag()
    {
        var record = Record(Guid.NewGuid(), isPublic: true);
        var regenerated = Record(record.Id, isPublic: true);
        regenerated.ThumbnailPath = "thumbnails/another-key.jpg";

        Assert.NotEqual(PreviewEtagHelper.ComputeThumbnail(record), PreviewEtagHelper.ComputeThumbnail(regenerated));
        Assert.NotEqual(PreviewEtagHelper.Compute(record), PreviewEtagHelper.ComputeThumbnail(record));
        Assert.DoesNotContain("thumbnails/", PreviewEtagHelper.ComputeThumbnail(record));
        Assert.Null(PreviewEtagHelper.ComputeThumbnail(new FileRecord { Id = record.Id, ThumbnailPath = null }));
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────

    private static FileRecord Record(Guid id, bool isPublic) => new()
    {
        Id = id,
        FileName = "server-key.png",
        OriginalName = "photo.png",
        Extension = ".png",
        ContentType = "image/png",
        Size = FileSize,
        IsPublic = isPublic,
        Md5Hash = "abc123",
        ThumbnailPath = "thumbnails/server-key.jpg"
    };

    private static byte[] ReadBody(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var written = new MemoryStream();
        http.Response.Body.CopyTo(written);
        return written.ToArray();
    }

    private static (HttpContext Http, DefaultStorageController Controller) NewController(Mock<IFileStorageService> storage)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvcCore();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Request.Method = HttpMethods.Get;
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
        return (http, controller);
    }

    private static async Task<(HttpContext Http, Mock<IFileStorageService> Storage)> ExecuteDownloadAsync(string range)
    {
        var id = Guid.NewGuid();
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.GetRecordAsync(id)).ReturnsAsync(Result.Success(Record(id, isPublic: true)));
        // 与内置 InMemory provider 同形：返回只含这一段的、可 seek 的 MemoryStream。
        storage.Setup(s => s.GetRangeAsync(id, It.IsAny<long?>(), It.IsAny<long?>()))
            .ReturnsAsync((Guid _, long? start, long? end) =>
            {
                var from = start ?? 0;
                var to = end ?? FileSize - 1;
                var slice = Content[(int)from..((int)to + 1)];
                return Result.Success<(Stream Stream, long Start, long End, long TotalLength)>((new MemoryStream(slice), from, to, FileSize));
            });

        var (http, controller) = NewController(storage);
        http.Request.Headers.Range = range;

        var result = await controller.Download(id);
        await result.ExecuteResultAsync(controller.ControllerContext);
        return (http, storage);
    }

    private static async Task<(HttpContext Http, Mock<IFileStorageService> Storage, FileRecord Record)> ExecuteThumbnailAsync(
        bool isPublic, string? ifNoneMatch = null, Guid? recordId = null)
    {
        var id = recordId ?? Guid.NewGuid();
        var record = Record(id, isPublic);
        var storage = new Mock<IFileStorageService>();
        storage.Setup(s => s.GetRecordAsync(id)).ReturnsAsync(Result.Success(record));
        storage.Setup(s => s.GetThumbnailAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Success<Stream>(new MemoryStream(Content)));

        var (http, controller) = NewController(storage);
        if (ifNoneMatch != null)
        {
            http.Request.Headers.IfNoneMatch = ifNoneMatch;
        }

        var result = await controller.GetThumbnail(id);
        await result.ExecuteResultAsync(controller.ControllerContext);
        return (http, storage, record);
    }
}
