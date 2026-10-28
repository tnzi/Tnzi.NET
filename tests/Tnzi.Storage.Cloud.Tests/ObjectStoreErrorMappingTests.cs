using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using StorageFileNotFoundException = Tnzi.Storage.Exceptions.FileNotFoundException;

namespace Tnzi.Storage.Cloud.Tests;

/// <summary>
/// S3 / R2 共用的对象操作（<see cref="S3ObjectStore"/>）怎么报「不存在」与「出错了」。
/// </summary>
/// <remarks>
/// <para>
/// 读一个不存在的对象此前抛裸的 <see cref="AmazonS3Exception"/>，上层答 500；Local / InMemory / Azure 抛的是框架的
/// <c>FileNotFoundException</c>，上层答 404。同一个情形（记录在、对象丢了）的回答取决于部署用的是哪个 provider。
/// </para>
/// <para>
/// <c>ExistsAsync</c> 此前对任何错误都答 <c>false</c>。删除路径在 <c>DeleteAsync</c> 报 <c>false</c> 之后靠它分辨「删失败了」
/// 与「本来就不在」：同一次网络故障让删除报 <c>false</c>、存在检查也报 <c>false</c>，记录随即被删，对象再也没有记录指向。
/// </para>
/// </remarks>
public class ObjectStoreErrorMappingTests
{
    private static AmazonS3Exception S3Error(HttpStatusCode status, string code)
        => new($"{code} from the service", Amazon.Runtime.ErrorType.Sender, code, "request-id", status);

    private static (S3ObjectStore Store, Mock<IAmazonS3> Client) Create()
    {
        var client = new Mock<IAmazonS3>();
        return (new S3ObjectStore(client.Object, "test-bucket", "S3", presignProtocol: null, logger: null), client);
    }

    [Fact]
    public async Task DownloadingAMissingObject_ThrowsTheFrameworksFileNotFound()
    {
        var (store, client) = Create();
        client.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.NotFound, "NoSuchKey"));

        var ex = await Assert.ThrowsAsync<StorageFileNotFoundException>(() => store.DownloadAsync("2026/10/11/gone.pdf"));

        Assert.Equal("2026/10/11/gone.pdf", ex.FilePath);
    }

    [Fact]
    public async Task ARangeOfAMissingObject_ThrowsTheFrameworksFileNotFound()
    {
        var (store, client) = Create();
        client.Setup(c => c.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.NotFound, "NotFound"));

        await Assert.ThrowsAsync<StorageFileNotFoundException>(() => store.DownloadRangeAsync("gone.pdf", 0, 9));
    }

    /// <summary>对照：别的错误不冒充「不存在」，原样抛出。</summary>
    [Fact]
    public async Task ADownloadFailureOtherThanNotFound_IsNotReportedAsMissing()
    {
        var (store, client) = Create();
        client.Setup(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.Forbidden, "AccessDenied"));

        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.DownloadAsync("secret.pdf"));
    }

    [Fact]
    public async Task Exists_IsFalse_OnlyWhenTheServiceSaysNotFound()
    {
        var (store, client) = Create();
        client.Setup(c => c.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.NotFound, "NotFound"));

        Assert.False(await store.ExistsAsync("gone.pdf"));
    }

    /// <summary>★★ 拿不准就不答「不在」：权限与网络故障抛出，交给调用方判断。</summary>
    [Fact]
    public async Task Exists_Throws_WhenItCannotTell()
    {
        var (store, client) = Create();
        client.SetupSequence(c => c.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.Forbidden, "AccessDenied"))
            .ThrowsAsync(new HttpRequestException("connection reset"));

        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.ExistsAsync("a.pdf"));
        await Assert.ThrowsAsync<HttpRequestException>(() => store.ExistsAsync("a.pdf"));
    }

    [Fact]
    public async Task Exists_IsTrue_WhenTheMetadataComesBack()
    {
        var (store, client) = Create();
        client.Setup(c => c.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectMetadataResponse());

        Assert.True(await store.ExistsAsync("a.pdf"));
    }
}
