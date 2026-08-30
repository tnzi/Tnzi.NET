namespace Tnzi.Storage.Tests;

/// <summary>
/// 未加载 <c>Tnzi.Storage.Cloud</c> 时，把 <c>Storage:Provider</c> 配成对象存储会发生什么。
/// </summary>
/// <remarks>
/// <para>
/// 本测试项目<b>刻意只引用 <c>Tnzi.Storage</c></b>，所以进程里根本没有那个子模块去往
/// <see cref="StorageProviderFactory"/> 注册 s3/r2/azure —— 这正是消费方少加载一个包时的现场，
/// 不需要额外搭夹具去模拟。
/// </para>
/// <para>
/// ★ 要守住的是<b>拒绝本身</b>，不是错误文案。回退到 Local 看起来更宽容，实际是把本该进对象存储的
/// 文件默默写到本地磁盘：配置、日志、接口返回全都正常，等到发现时盘上已经堆了一批没人预期的文件，
/// 而且没有任何一条记录说得清它们为什么在那里。所以这里同时断言「抛了」与「抛出来的不是 LocalStorage」。
/// </para>
/// </remarks>
public class CloudProviderAbsenceTests
{
    private static StorageProviderContext ContextFor(string provider)
    {
        var options = new StorageOptions
        {
            Provider = provider,
            S3 = new S3StorageOptions
            {
                AccessKeyId = "fake-access-key",
                SecretAccessKey = "fake-secret-key",
                BucketName = "test-bucket",
                Region = "us-east-1"
            },
            R2 = new R2StorageOptions
            {
                AccessKeyId = "fake-access-key",
                SecretAccessKey = "fake-secret-key",
                BucketName = "test-bucket",
                AccountId = "fakeaccount123"
            },
            Azure = new AzureBlobStorageOptions
            {
                ConnectionString = "UseDevelopmentStorage=true",
                ContainerName = "test-container"
            }
        };

        var configuration = new ConfigurationBuilder().Build();
        return new StorageProviderContext(options, configuration);
    }

    [Theory]
    [InlineData("S3")]
    [InlineData("R2")]
    [InlineData("Azure")]
    public void CloudProvider_WithoutTheCloudModule_ThrowsAndNamesTheModule(string provider)
    {
        // 选项配得齐齐整整：这里缺的不是配置，是那个包。
        var ex = Assert.Throws<InvalidOperationException>(() => StorageProviderFactory.Create(ContextFor(provider)));

        Assert.Contains("Tnzi.Storage.Cloud", ex.Message, StringComparison.Ordinal);
        Assert.Contains("StorageCloudModule", ex.Message, StringComparison.Ordinal);
        // 已注册的 provider 要列出来，否则拿到报错的人不知道自己还剩什么可用。
        Assert.Contains("local", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>★ 绝不悄悄改用本地磁盘 —— 那会把文件写到没人预期的地方而全程无异常。</summary>
    [Theory]
    [InlineData("S3")]
    [InlineData("R2")]
    [InlineData("Azure")]
    public void CloudProvider_WithoutTheCloudModule_NeverFallsBackToLocalDisk(string provider)
    {
        IFileStorage? created = null;

        try
        {
            created = StorageProviderFactory.Create(ContextFor(provider));
        }
        catch (InvalidOperationException)
        {
            // 期望的路径。
        }

        Assert.Null(created);
    }

    /// <summary>名字打错时的报错不该扯上子模块，否则会把人引去装一个装了也没用的包。</summary>
    [Fact]
    public void UnknownProvider_DoesNotBlameTheCloudModule()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => StorageProviderFactory.Create(ContextFor("s4")));

        Assert.DoesNotContain("Tnzi.Storage.Cloud", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>本模块自带的两个 provider 不受拆分影响。</summary>
    [Theory]
    [InlineData("Local")]
    [InlineData("InMemory")]
    public void BuiltInProviders_StillResolve(string provider)
    {
        Assert.True(StorageProviderFactory.IsRegistered(provider));
    }
}
