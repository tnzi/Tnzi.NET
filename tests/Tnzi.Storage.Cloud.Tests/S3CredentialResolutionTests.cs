using Amazon.Runtime;

namespace Tnzi.Storage.Cloud.Tests;

/// <summary>
/// Tests that mutate the AWS environment for the whole process.
///
/// ★ Two kinds of shared state make this necessary, and neither is visible from
/// the test body: the environment variables themselves, and
/// <c>FallbackCredentialsFactory</c>'s <b>static cache</b> of whatever the chain
/// resolved first. xUnit runs test classes in parallel by default, so without a
/// collection a second class asking for default-chain credentials would either
/// race these variables or silently be handed the fake identity they left
/// behind - and which of the two you got would depend on scheduling.
/// </summary>
[CollectionDefinition(AwsEnvironmentCollection.Name, DisableParallelization = true)]
public class AwsEnvironmentCollection
{
    public const string Name = "AWS environment";
}

/// <summary>
/// S3 凭据解析的三态契约：两个密钥字段都填 = 静态密钥；都留空 = 交给 AWS SDK 的默认凭据链
/// （环境变量 / 共享凭据文件 / ECS 任务角色 / EC2 实例角色）；只填一个 = 笔误，拒绝。
///
/// 验证器与 provider 是这条规则的两半，所以钉在同一个文件里：一半松了另一半没松，
/// 症状是「启动过了、第一次访问对象存储才炸」，而那时离配置现场已经很远。
///
/// ★ provider 拆到 <c>Tnzi.Storage.Cloud</c> 之后这个文件<b>整份</b>跟着过来，
/// 而不是按程序集切成两半：验证器（仍在 <c>Tnzi.Storage</c>）与 provider 分开来看
/// 各自都是绿的，这批用例守的恰恰是它们之间的口径一致。子项目引用得到父模块，
/// 所以两半仍能写在一处。
/// </summary>
[Collection(AwsEnvironmentCollection.Name)]
public class S3CredentialResolutionTests
{
    private static StorageOptions S3Options(
        string accessKeyId,
        string secretAccessKey,
        string bucketName = "test-bucket",
        string region = "us-east-1")
        => new()
        {
            Provider = "S3",
            S3 = new S3StorageOptions
            {
                AccessKeyId = accessKeyId,
                SecretAccessKey = secretAccessKey,
                BucketName = bucketName,
                Region = region
            }
        };

    private static ValidateOptionsResult Validate(StorageOptions options)
        => new StorageOptionsValidator().Validate(null, options);

    #region 三态判定

    [Fact]
    public void UsesDefaultCredentialChain_IsTrue_OnlyWhenBothKeysAreBlank()
    {
        Assert.True(new S3StorageOptions().UsesDefaultCredentialChain);
        Assert.True(new S3StorageOptions { AccessKeyId = "  ", SecretAccessKey = "	" }.UsesDefaultCredentialChain);

        // 半填不是在要默认凭据链，是打错了。
        Assert.False(new S3StorageOptions { AccessKeyId = "AKIAEXAMPLE" }.UsesDefaultCredentialChain);
        Assert.False(new S3StorageOptions { SecretAccessKey = "example-secret" }.UsesDefaultCredentialChain);

        Assert.False(new S3StorageOptions { AccessKeyId = "AKIAEXAMPLE", SecretAccessKey = "example-secret" }
            .UsesDefaultCredentialChain);
    }

    #endregion

    #region 验证器

    [Fact]
    public void Validator_S3_WithBothKeysEmpty_IsAccepted()
    {
        // ECS 任务角色 / EC2 实例角色部署：配置里根本不该出现一对长期密钥。
        var result = Validate(S3Options("", ""));

        Assert.False(result.Failed, string.Join(" | ", result.Failures ?? Array.Empty<string>()));
    }

    [Fact]
    public void Validator_S3_WithBothKeysSupplied_IsAccepted()
    {
        // MinIO / 自托管 S3 兼容服务的既有用法，行为逐字不变。
        var result = Validate(S3Options("AKIAEXAMPLE", "example-secret"));

        Assert.False(result.Failed, string.Join(" | ", result.Failures ?? Array.Empty<string>()));
    }

    [Fact]
    public void Validator_S3_WithOnlyAccessKeyId_IsRejected()
    {
        var result = Validate(S3Options("AKIAEXAMPLE", ""));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("S3.SecretAccessKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_S3_WithOnlySecretAccessKey_IsRejected()
    {
        var result = Validate(S3Options("", "example-secret"));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("S3.AccessKeyId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_S3_WithoutStaticCredentials_StillRequiresBucketAndRegion()
    {
        // 放开的只有密钥这一条，别的必填项不受影响。
        var result = Validate(S3Options("", "", bucketName: "", region: ""));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("S3.BucketName", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("S3.Region", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_R2_StillRequiresStaticCredentials()
    {
        // R2 没有实例角色这回事，它的必填项刻意不动。
        var options = new StorageOptions
        {
            Provider = "R2",
            R2 = new R2StorageOptions { BucketName = "test-bucket", AccountId = "fakeaccount123" }
        };

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("R2.AccessKeyId", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("R2.SecretAccessKey", StringComparison.Ordinal));
    }

    #endregion

    #region Provider

    [Fact]
    public async Task S3Storage_WithoutStaticCredentials_SignsWithChainResolvedCredentials()
    {
        // 默认凭据链的第一站是环境变量，所以在进程里放一对，就能离线证明
        // 「没给静态密钥时用的确实是链解析出来的凭据」：签出来的 URL 里带的是链里那把 key id。
        // 只断言「构造没抛异常」证明不了这一点 —— 那种断言在客户端根本没解析凭据时也是绿的。
        // 刻意不用 AKIA 开头：这个值只需要在签出来的 URL 里能被认出来，
        // 而 AKIA 前缀会命中公开镜像的凭据扫描（那道门禁只看形状，不看语义），
        // 逼着有人去签一条「这是假的」豁免 —— 而豁免清单每多一条，
        // 真密钥混进占位符堆里的机会就多一分。换个不像密钥的值就没有这个问题。
        const string chainKeyId = "TNZI-TEST-CHAIN-RESOLVED-ID";

        var savedKeyId = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var savedSecret = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        var savedToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");
        var savedProfile = Environment.GetEnvironmentVariable("AWS_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", chainKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "chain-resolved-secret");
            Environment.SetEnvironmentVariable("AWS_SESSION_TOKEN", null);
            // 开发机上多半存在一份共享凭据文件，指一个不存在的 profile，
            // 让这条用例的结果不取决于跑它的机器上有没有 ~/.aws/credentials。
            Environment.SetEnvironmentVariable("AWS_PROFILE", "tnzi-nonexistent-profile-for-tests");

            // The SDK caches the first chain resolution for the life of the process,
            // so without this the variables above would be ignored whenever some
            // earlier code had already resolved credentials.
            FallbackCredentialsFactory.Reset();

            using var storage = new S3Storage(new S3StorageOptions
            {
                BucketName = "test-bucket",
                Region = "us-east-1"
            });

            var url = await storage.GetPresignedUrlAsync("some/key.jpg", 3600, "GET");

            Assert.False(string.IsNullOrEmpty(url));
            Assert.Contains("X-Amz-Signature", url!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(chainKeyId, url, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", savedKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", savedSecret);
            Environment.SetEnvironmentVariable("AWS_SESSION_TOKEN", savedToken);
            Environment.SetEnvironmentVariable("AWS_PROFILE", savedProfile);
            // Leave no fake identity cached for anything that runs after this.
            FallbackCredentialsFactory.Reset();
        }
    }

    [Fact]
    public void S3Storage_WithHalfFilledCredentials_Throws()
    {
        // provider 也拦一次：直接 new S3Storage(...) 的调用方绕过了选项验证器。
        Assert.Throws<ArgumentException>(() => new S3Storage(new S3StorageOptions
        {
            AccessKeyId = "AKIAEXAMPLE",
            BucketName = "test-bucket",
            Region = "us-east-1"
        }));

        Assert.Throws<ArgumentException>(() => new S3Storage(new S3StorageOptions
        {
            SecretAccessKey = "example-secret",
            BucketName = "test-bucket",
            Region = "us-east-1"
        }));

        // 空白串与验证器口径一致：它既不算静态密钥，也不算在要默认凭据链。
        Assert.Throws<ArgumentException>(() => new S3Storage(new S3StorageOptions
        {
            AccessKeyId = "   ",
            SecretAccessKey = "example-secret",
            BucketName = "test-bucket",
            Region = "us-east-1"
        }));
    }

    #endregion
}
