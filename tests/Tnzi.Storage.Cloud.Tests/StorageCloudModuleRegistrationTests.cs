namespace Tnzi.Storage.Cloud.Tests;

/// <summary>
/// 拆分引入的那条新接缝：三个 provider 的名字现在<b>不在</b><see cref="StorageProviderFactory"/> 的静态构造里，
/// 改由本模块注册。这批用例证明它真的注册了，而且注册出来的是原来那三个类。
/// </summary>
/// <remarks>
/// <para>
/// 没有这批用例，「模块忘了注册」的表现是：加载了包、配置写得好好的、启动一声不响，
/// 直到第一次存文件才抛「未知的 provider」。它与「压根没加载这个包」的现场一模一样，
/// 而两者该看的地方完全不同。
/// </para>
/// <para>
/// 名字必须与拆分前逐字相同（大小写不敏感），否则既有部署的 <c>Storage:Provider</c> 会突然失效。
/// </para>
/// </remarks>
public class StorageCloudModuleRegistrationTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    /// <summary>装配一次子模块，与启动时走的是同一个入口。</summary>
    private static async Task LoadModuleAsync()
    {
        var context = new ServiceConfigurationContext(new ServiceCollection(), EmptyConfiguration);
        await new StorageCloudModule().PreConfigureServicesAsync(context);
    }

    private static StorageProviderContext ContextFor(string provider) => new(
        new StorageOptions
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
                ConnectionString = FakeAzureConnectionString,
                ContainerName = "test-container"
            }
        },
        EmptyConfiguration);

    // 语法上合法的假账号密钥，Azure 客户端在构造期就要解析连接串。
    private static readonly string FakeAzureConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=fakeaccount;AccountKey="
        + Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-azure-account-key-for-local-signing-0123456789"))
        + ";EndpointSuffix=core.windows.net";

    [Theory]
    [InlineData("s3", typeof(S3Storage))]
    [InlineData("r2", typeof(R2Storage))]
    [InlineData("azure", typeof(AzureBlobStorage))]
    public async Task Module_RegistersEachProviderUnderItsExistingName(string provider, Type expected)
    {
        await LoadModuleAsync();

        StorageProviderFactory.IsRegistered(provider).ShouldBeTrue();

        var storage = StorageProviderFactory.Create(ContextFor(provider));
        try
        {
            storage.ShouldBeOfType(expected);
        }
        finally
        {
            (storage as IDisposable)?.Dispose();
        }
    }

    /// <summary>名字大小写不敏感：既有配置里写的是 <c>"S3"</c> / <c>"Azure"</c>。</summary>
    [Theory]
    [InlineData("S3")]
    [InlineData("R2")]
    [InlineData("Azure")]
    public async Task RegisteredNames_AreCaseInsensitive(string provider)
    {
        await LoadModuleAsync();

        StorageProviderFactory.IsRegistered(provider).ShouldBeTrue();
    }

    /// <summary>父模块自带的两个 provider 不受影响，注册不是覆盖式的。</summary>
    [Fact]
    public async Task Module_DoesNotDisturbTheBuiltInProviders()
    {
        await LoadModuleAsync();

        StorageProviderFactory.IsRegistered("local").ShouldBeTrue();
        StorageProviderFactory.IsRegistered("inmemory").ShouldBeTrue();
    }

    /// <summary>选项缺失时报的是「缺 S3 配置」，而不是让 provider 拿着 null 往下走。</summary>
    [Fact]
    public async Task Provider_WithoutItsOptions_FailsWithANamedReason()
    {
        await LoadModuleAsync();

        var context = new StorageProviderContext(new StorageOptions { Provider = "s3" }, EmptyConfiguration);

        var ex = Assert.Throws<InvalidOperationException>(() => StorageProviderFactory.Create(context));
        ex.Message.ShouldContain("S3 options are required");
    }
}
