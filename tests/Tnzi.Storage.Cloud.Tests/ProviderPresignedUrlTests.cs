
namespace Tnzi.Storage.Cloud.Tests;

/// <summary>
/// Provider-level tests for <c>GetPresignedUrlAsync</c> overrides on the cloud storage providers
/// (S3 / R2 use AWS SDK signing; Azure uses SAS). These run fully offline with fake credentials -
/// signing is a local cryptographic operation that does not touch the network.
/// </summary>
public class ProviderPresignedUrlTests
{
    // A syntactically valid base64-encoded fake account key so Azure SAS signing works locally.
    private static readonly string FakeAzureAccountKey =
        Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-azure-account-key-for-local-signing-0123456789"));

    private static R2Storage CreateR2Storage()
    {
        var options = new R2StorageOptions
        {
            AccessKeyId = "fake-access-key",
            SecretAccessKey = "fake-secret-key",
            BucketName = "test-bucket",
            AccountId = "fakeaccount123"
        };
        return new R2Storage(options);
    }

    private static AzureBlobStorage CreateAzureStorage()
    {
        var connectionString =
            $"DefaultEndpointsProtocol=https;AccountName=fakeaccount;AccountKey={FakeAzureAccountKey};EndpointSuffix=core.windows.net";
        var options = new AzureBlobStorageOptions
        {
            ConnectionString = connectionString,
            ContainerName = "test-container"
        };
        return new AzureBlobStorage(options);
    }

    #region R2

    [Fact]
    public async Task R2_GetPresignedUrlAsync_Put_ReturnsSignedUrl()
    {
        using var storage = CreateR2Storage();

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "PUT");

        Assert.False(string.IsNullOrEmpty(url));
        // AWS SigV4 presigned URLs carry the signature query parameter.
        Assert.Contains("X-Amz-Signature", url!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("some/key.jpg", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R2_GetPresignedUrlAsync_Get_ReturnsSignedUrl()
    {
        using var storage = CreateR2Storage();

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "GET");

        Assert.False(string.IsNullOrEmpty(url));
        Assert.Contains("X-Amz-Signature", url!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Amz-Expires", url, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The signed URL has to point at the account's R2 endpoint. In the AWS SDK, RegionEndpoint and
    /// ServiceURL are mutually exclusive and whichever is set last clears the other, so a client
    /// configured with both talks to AWS S3 and every R2 request fails with a key the server does not know.
    /// </summary>
    [Fact]
    public async Task R2_GetPresignedUrlAsync_PointsAtTheAccountEndpoint()
    {
        using var storage = CreateR2Storage();

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "GET");

        var uri = new Uri(url!);
        Assert.Equal("fakeaccount123.r2.cloudflarestorage.com", uri.Host);
        Assert.StartsWith("/test-bucket/some/key.jpg", uri.AbsolutePath, StringComparison.Ordinal);
        // R2 signs for its single logical region.
        Assert.Contains("%2Fauto%2Fs3%2Faws4_request", uri.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task R2_GetPresignedUrlAsync_CustomEndpoint_IsUsedAsIs()
    {
        using var storage = new R2Storage(new R2StorageOptions
        {
            AccessKeyId = "fake-access-key",
            SecretAccessKey = "fake-secret-key",
            BucketName = "test-bucket",
            CustomEndpoint = "https://r2.example.test"
        });

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "PUT");

        Assert.Equal("r2.example.test", new Uri(url!).Host);
    }

    [Fact]
    public async Task R2_GetPresignedUrlAsync_EmptyPath_ReturnsNull()
    {
        using var storage = CreateR2Storage();

        var url = await storage.GetPresignedUrlAsync("", 3600, "GET");

        Assert.Null(url);
    }

    #endregion

    #region Azure

    [Fact]
    public async Task Azure_GetPresignedUrlAsync_Put_ReturnsSasUrl()
    {
        var storage = CreateAzureStorage();

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "PUT");

        Assert.False(string.IsNullOrEmpty(url));
        // Azure SAS URLs carry the "sig=" signature query parameter.
        Assert.Contains("sig=", url!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Azure_GetPresignedUrlAsync_Get_ReturnsSasUrl()
    {
        var storage = CreateAzureStorage();

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "GET");

        Assert.False(string.IsNullOrEmpty(url));
        Assert.Contains("sig=", url!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("some/key.jpg", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Azure_GetPresignedUrlAsync_EmptyPath_ReturnsNull()
    {
        var storage = CreateAzureStorage();

        var url = await storage.GetPresignedUrlAsync("", 3600, "GET");

        Assert.Null(url);
    }

    #endregion

    #region S3 (regression - existing override + UseHttps wiring)

    [Fact]
    public async Task S3_GetPresignedUrlAsync_Put_ReturnsSignedUrl()
    {
        var options = new S3StorageOptions
        {
            AccessKeyId = "fake-access-key",
            SecretAccessKey = "fake-secret-key",
            BucketName = "test-bucket",
            Region = "us-east-1"
        };
        using var storage = new S3Storage(options);

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "PUT");

        Assert.False(string.IsNullOrEmpty(url));
        Assert.Contains("X-Amz-Signature", url!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task S3_WithCustomServiceUrl_UseHttpsFalse_PresignedUrlUsesHttp()
    {
        var options = new S3StorageOptions
        {
            AccessKeyId = "fake-access-key",
            SecretAccessKey = "fake-secret-key",
            BucketName = "test-bucket",
            Region = "us-east-1",
            ServiceUrl = "minio.local:9000",
            UseHttps = false
        };
        using var storage = new S3Storage(options);

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "GET");

        Assert.False(string.IsNullOrEmpty(url));
        Assert.StartsWith("http://", url!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task S3_WithCustomServiceUrl_UseHttpsTrue_PresignedUrlUsesHttps()
    {
        var options = new S3StorageOptions
        {
            AccessKeyId = "fake-access-key",
            SecretAccessKey = "fake-secret-key",
            BucketName = "test-bucket",
            Region = "us-east-1",
            ServiceUrl = "minio.local:9000",
            UseHttps = true
        };
        using var storage = new S3Storage(options);

        var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "GET");

        Assert.False(string.IsNullOrEmpty(url));
        Assert.StartsWith("https://", url!, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Read URLs for active content are attachments

    /// <summary>
    /// ★★ 预签名的读 URL 由桶直接服务，类型是上传时按上传者给的文件名定下的。<c>.html</c> / <c>.svg</c> 不加处置，
    /// 浏览器打开这条链接就在桶的源上渲染并执行其中的脚本。与 <c>files/{id}/preview</c> 同一张白名单：之外的类型按附件处置。
    /// </summary>
    [Theory]
    [InlineData("S3", "2026/10/11/payload.html")]
    [InlineData("S3", "2026/10/11/logo.svg")]
    [InlineData("R2", "2026/10/11/payload.html")]
    [InlineData("R2", "2026/10/11/feed.xml")]
    public async Task AReadUrlForActiveContent_AsksForAnAttachment(string provider, string key)
    {
        var storage = CreateS3Compatible(provider);
        using var lifetime = (IDisposable)storage;

        var url = await storage.GetPresignedUrlAsync(key, 3600, "GET");

        Assert.Contains("response-content-disposition=attachment", url!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("S3", "2026/10/11/photo.png")]
    [InlineData("S3", "2026/10/11/contract.pdf")]
    [InlineData("R2", "2026/10/11/clip.mp4")]
    public async Task AReadUrlForDisplayableContent_StaysInline(string provider, string key)
    {
        var storage = CreateS3Compatible(provider);
        using var lifetime = (IDisposable)storage;

        var url = await storage.GetPresignedUrlAsync(key, 3600, "GET");

        Assert.DoesNotContain("response-content-disposition", url!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>直传 URL 不带处置：它不是给浏览器打开的，请求头里也没有处置这一说。</summary>
    [Theory]
    [InlineData("S3")]
    [InlineData("R2")]
    public async Task AnUploadUrl_CarriesNoDisposition(string provider)
    {
        var storage = CreateS3Compatible(provider);
        using var lifetime = (IDisposable)storage;

        var url = await storage.GetPresignedUrlAsync("2026/10/11/payload.html", 3600, "PUT");

        Assert.DoesNotContain("response-content-disposition", url!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnAzureReadUrlForActiveContent_AsksForAnAttachment()
    {
        var storage = CreateAzureStorage();

        var html = await storage.GetPresignedUrlAsync("2026/10/11/payload.html", 3600, "GET");
        var png = await storage.GetPresignedUrlAsync("2026/10/11/photo.png", 3600, "GET");
        var upload = await storage.GetPresignedUrlAsync("2026/10/11/payload.html", 3600, "PUT");

        Assert.Contains("rscd=attachment", html!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rscd=", png!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rscd=", upload!, StringComparison.OrdinalIgnoreCase);
    }

    private static IFileStorage CreateS3Compatible(string provider) => provider switch
    {
        "S3" => new S3Storage(new S3StorageOptions
        {
            AccessKeyId = "fake-access-key",
            SecretAccessKey = "fake-secret-key",
            BucketName = "test-bucket",
            Region = "us-east-1"
        }),
        "R2" => CreateR2Storage(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    #endregion
}
