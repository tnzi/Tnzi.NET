
namespace Tnzi.Storage.Tests;

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
}
