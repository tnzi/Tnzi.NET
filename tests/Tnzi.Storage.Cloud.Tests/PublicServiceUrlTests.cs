namespace Tnzi.Storage.Cloud.Tests;

/// <summary>
/// <c>PublicServiceUrl</c>：应用经私网地址访问存储、浏览器经公开主机访问同一个存储时，预签名 URL 对公开主机签名。
///
/// SigV4 签名覆盖 Host，所以「签完再改写主机」的链接存储一律拒收；这批用例断言链接从签名那一刻起就指向公开主机，
/// 而签名区域与私网那一路相同（否则存储接受一路、拒绝另一路）。验证器与 provider 是这条设置的两半，钉在同一个文件里。
/// 全部离线跑：签名是本地的密码学运算，不碰网络。
/// </summary>
public class PublicServiceUrlTests
{
    private const string PrivateServiceUrl = "http://10.200.7.2:3900";
    private const string PublicServiceUrl = "https://s3.example.com";

    private static S3Storage CreateS3(string? publicServiceUrl, string serviceUrl = PrivateServiceUrl, string region = "us-east-1")
        => new(new S3StorageOptions
        {
            AccessKeyId = "fake-access-key",
            SecretAccessKey = "fake-secret-key",
            BucketName = "test-bucket",
            Region = region,
            ServiceUrl = serviceUrl,
            UseHttps = !serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase),
            PublicServiceUrl = publicServiceUrl
        });

    private static R2Storage CreateR2(string? publicServiceUrl)
        => new(new R2StorageOptions
        {
            AccessKeyId = "fake-access-key",
            SecretAccessKey = "fake-secret-key",
            BucketName = "test-bucket",
            AccountId = "fakeaccount123",
            PublicServiceUrl = publicServiceUrl
        });

    /// <summary>X-Amz-Credential 的作用域 <c>{key}/{date}/{region}/s3/aws4_request</c> 里的区域。</summary>
    private static string SigningRegionOf(Uri url)
    {
        var credential = Uri.UnescapeDataString(
            url.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("X-Amz-Credential=", StringComparison.Ordinal))
                ["X-Amz-Credential=".Length..]);
        return credential.Split('/')[2];
    }

    #region S3

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task S3_WithPublicServiceUrl_SignsForThePublicHost(string verb)
    {
        using var storage = CreateS3(PublicServiceUrl);

        var url = new Uri((await storage.GetPresignedUrlAsync("2026/11/07/photo.png", 3600, verb))!);

        Assert.Equal("https", url.Scheme);
        Assert.Equal("s3.example.com", url.Host);
        Assert.True(url.IsDefaultPort);
        // 路径样式：桶名在路径里，不被提升成公开主机的子域名。
        Assert.Equal("/test-bucket/2026/11/07/photo.png", url.AbsolutePath);
        Assert.Contains("X-Amz-Signature=", url.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task S3_WithoutPublicServiceUrl_SignsForTheServiceUrl(string verb)
    {
        using var storage = CreateS3(publicServiceUrl: null);

        var url = new Uri((await storage.GetPresignedUrlAsync("2026/11/07/photo.png", 3600, verb))!);

        Assert.Equal("http", url.Scheme);
        Assert.Equal("10.200.7.2", url.Host);
        Assert.Equal(3900, url.Port);
    }

    [Fact]
    public async Task S3_BlankPublicServiceUrl_ChangesNothing()
    {
        using var storage = CreateS3(publicServiceUrl: "  ");

        var url = new Uri((await storage.GetPresignedUrlAsync("photo.png", 3600, "GET"))!);

        Assert.Equal("10.200.7.2", url.Host);
    }

    /// <summary>
    /// ★ 没有显式签名区域时 SDK 从主机名推区域：私网 IP 推成 us-east-1，<c>s3.eu-west-1.example.com</c> 却推成 eu-west-1。
    /// 公开那一路必须沿用私网那一路的区域，存储才会两路都认。
    /// </summary>
    [Fact]
    public async Task S3_PublicLink_IsSignedForTheSameRegionAsThePrivateOne()
    {
        using var publicStorage = CreateS3("https://s3.eu-west-1.example.com");
        using var privateStorage = CreateS3(publicServiceUrl: null);

        var publicUrl = new Uri((await publicStorage.GetPresignedUrlAsync("photo.png", 3600, "GET"))!);
        var privateUrl = new Uri((await privateStorage.GetPresignedUrlAsync("photo.png", 3600, "GET"))!);

        Assert.Equal("s3.eu-west-1.example.com", publicUrl.Host);
        Assert.Equal(SigningRegionOf(privateUrl), SigningRegionOf(publicUrl));
    }

    [Fact]
    public async Task S3_OnTheAwsEndpoint_PublicLink_KeepsTheConfiguredRegion()
    {
        using var storage = CreateS3("https://files.example.com", serviceUrl: "", region: "eu-central-1");

        var url = new Uri((await storage.GetPresignedUrlAsync("photo.png", 3600, "GET"))!);

        Assert.Equal("files.example.com", url.Host);
        Assert.Equal("eu-central-1", SigningRegionOf(url));
    }

    /// <summary>私网走 https、公开地址是 http（或反过来）时，链接的 scheme 取公开地址的，而不是 UseHttps 的。</summary>
    [Fact]
    public async Task S3_PublicLink_TakesTheSchemeAndPortOfThePublicUrl()
    {
        using var storage = CreateS3("http://s3.example.com:8080", serviceUrl: "https://storage.internal");

        var url = new Uri((await storage.GetPresignedUrlAsync("photo.png", 3600, "PUT"))!);

        Assert.Equal("http", url.Scheme);
        Assert.Equal("s3.example.com", url.Host);
        Assert.Equal(8080, url.Port);
    }

    [Fact]
    public async Task S3_PublicLink_KeepsTheAttachmentDispositionForActiveContent()
    {
        using var storage = CreateS3(PublicServiceUrl);

        var url = await storage.GetPresignedUrlAsync("2026/11/07/payload.html", 3600, "GET");

        Assert.Contains("response-content-disposition=attachment", url!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>公开端点只管签名：不带过期的基础 URL 仍按 UrlPrefix / 默认规则拼，不改走公开地址。</summary>
    [Fact]
    public async Task S3_UnsignedUrl_IsNotAffected()
    {
        using var storage = CreateS3(PublicServiceUrl);

        var url = await storage.GetUrlAsync("photo.png");

        Assert.DoesNotContain("s3.example.com", url, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("s3.example.com")]
    [InlineData("ftp://s3.example.com")]
    [InlineData("not a url")]
    public void S3_ConstructedDirectlyWithAnInvalidPublicServiceUrl_Throws(string publicServiceUrl)
    {
        Assert.Throws<ArgumentException>(() => CreateS3(publicServiceUrl));
    }

    #endregion

    #region R2

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task R2_WithPublicServiceUrl_SignsForThePublicHostInRegionAuto(string verb)
    {
        using var storage = CreateR2(PublicServiceUrl);

        var url = new Uri((await storage.GetPresignedUrlAsync("2026/11/07/photo.png", 3600, verb))!);

        Assert.Equal("https", url.Scheme);
        Assert.Equal("s3.example.com", url.Host);
        Assert.Equal("/test-bucket/2026/11/07/photo.png", url.AbsolutePath);
        Assert.Equal("auto", SigningRegionOf(url));
    }

    [Fact]
    public async Task R2_WithoutPublicServiceUrl_SignsForTheAccountEndpoint()
    {
        using var storage = CreateR2(publicServiceUrl: null);

        var url = new Uri((await storage.GetPresignedUrlAsync("photo.png", 3600, "GET"))!);

        Assert.Equal("fakeaccount123.r2.cloudflarestorage.com", url.Host);
        Assert.Equal("auto", SigningRegionOf(url));
    }

    #endregion

    #region 验证器

    private static ValidateOptionsResult ValidateS3(string? publicServiceUrl)
        => new StorageOptionsValidator().Validate(null, new StorageOptions
        {
            Provider = "S3",
            S3 = new S3StorageOptions
            {
                AccessKeyId = "fake-access-key",
                SecretAccessKey = "fake-secret-key",
                BucketName = "test-bucket",
                Region = "us-east-1",
                ServiceUrl = PrivateServiceUrl,
                PublicServiceUrl = publicServiceUrl
            }
        });

    private static ValidateOptionsResult ValidateR2(string? publicServiceUrl)
        => new StorageOptionsValidator().Validate(null, new StorageOptions
        {
            Provider = "R2",
            R2 = new R2StorageOptions
            {
                AccessKeyId = "fake-access-key",
                SecretAccessKey = "fake-secret-key",
                BucketName = "test-bucket",
                AccountId = "fakeaccount123",
                PublicServiceUrl = publicServiceUrl
            }
        });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://s3.example.com")]
    [InlineData("https://s3.example.com/")]
    [InlineData("http://s3.example.com:8080")]
    public void Validator_AcceptsAnEmptyOrAbsoluteHostUrl(string? publicServiceUrl)
    {
        Assert.True(ValidateS3(publicServiceUrl).Succeeded);
        Assert.True(ValidateR2(publicServiceUrl).Succeeded);
    }

    [Theory]
    [InlineData("s3.example.com")]
    [InlineData("ftp://s3.example.com")]
    [InlineData("https://example.com/s3")]
    [InlineData("https://s3.example.com/?x=1")]
    public void Validator_RejectsAnythingButSchemeHostAndPort(string publicServiceUrl)
    {
        var s3 = ValidateS3(publicServiceUrl);
        var r2 = ValidateR2(publicServiceUrl);

        Assert.True(s3.Failed);
        Assert.Contains("S3.PublicServiceUrl", s3.FailureMessage, StringComparison.Ordinal);
        Assert.True(r2.Failed);
        Assert.Contains("R2.PublicServiceUrl", r2.FailureMessage, StringComparison.Ordinal);
    }

    #endregion
}
