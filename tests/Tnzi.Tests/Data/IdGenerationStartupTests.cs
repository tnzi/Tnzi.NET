using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Tnzi.Data.Snows;

namespace Tnzi.Tests.Data;

/// <summary>
/// 雪花 WorkerId 是<b>部署参数</b>：多实例部署不可能静默共用一个 WorkerId，开发单机零配置仍可跑。
/// </summary>
/// <remarks>
/// 缺陷形态：全仓没有任何配置入口能设置 WorkerId，<c>IdHelper.NextId</c> 静默回退 WorkerId=1，
/// 两个副本在同一毫秒各自生成本毫秒的第一个 id 时三元组 (tick, 1, 5) 逐位相同 ——
/// long 主键冲突、Payment 单号重号，单实例永不复现。
/// 现在 <see cref="CoreServicesModule"/> 读 <c>IdGeneration</c> 节初始化生成器；
/// 未配置时 Production 启动即失败并指名要配的键，其它环境记 Warning 用默认值。
/// <para>
/// <see cref="IdHelper"/> 是进程级静态，本类各用例串行（同一测试类）且每次都显式重设生成器。
/// </para>
/// </remarks>
public class IdGenerationStartupTests
{
    private static IConfiguration BuildConfiguration(IDictionary<string, string?>? settings = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();

    private static ServiceProvider ConfigureAndBuild(CoreServicesModule module, IConfiguration configuration, string? hostEnvironment)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        if (hostEnvironment != null)
        {
            var env = new Mock<IHostEnvironment>();
            env.SetupGet(e => e.EnvironmentName).Returns(hostEnvironment);
            services.AddSingleton(env.Object);
        }

        var context = new ServiceConfigurationContext(services, configuration, hostEnvironment);
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services.BuildServiceProvider();
    }

    private static long ExtractWorkerId(long id, byte workerIdBitLength = 6, byte seqBitLength = 6) =>
        (id >> seqBitLength) & ((1L << workerIdBitLength) - 1);

    [Fact]
    public void ConfigureServices_WithWorkerId_GeneratesIdsCarryingThatWorkerId()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["IdGeneration:WorkerId"] = "37" });

        using var provider = ConfigureAndBuild(new CoreServicesModule(), configuration, "Production");

        var id = IdHelper.NextId();
        Assert.Equal(37, ExtractWorkerId(id));
    }

    [Fact]
    public void ConfigureServices_WithCustomBitLengths_HonoursThem()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["IdGeneration:WorkerId"] = "500",
            ["IdGeneration:WorkerIdBitLength"] = "10",
            ["IdGeneration:SeqBitLength"] = "8",
        });

        using var provider = ConfigureAndBuild(new CoreServicesModule(), configuration, "Production");

        var id = IdHelper.NextId();
        Assert.Equal(500, ExtractWorkerId(id, workerIdBitLength: 10, seqBitLength: 8));
    }

    [Fact]
    public async Task Initialization_ProductionWithoutWorkerId_FailsFastNamingTheKey()
    {
        var module = new CoreServicesModule();
        using var provider = ConfigureAndBuild(module, BuildConfiguration(), "Production");

        var ex = await Assert.ThrowsAsync<ConfigurationException>(() =>
            module.OnApplicationInitializationAsync(new ApplicationInitializationContext(provider)));

        Assert.Contains("IdGeneration:WorkerId", ex.Message);
        Assert.Contains("WorkerIdFromHostname", ex.Message);
    }

    [Fact]
    public async Task Initialization_ProductionWithWorkerId_Starts()
    {
        var module = new CoreServicesModule();
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["IdGeneration:WorkerId"] = "2" });
        using var provider = ConfigureAndBuild(module, configuration, "Production");

        await module.OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }

    [Fact]
    public async Task Initialization_DevelopmentWithoutWorkerId_StartsWithDefault()
    {
        var module = new CoreServicesModule();
        using var provider = ConfigureAndBuild(module, BuildConfiguration(), "Development");

        await module.OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));

        Assert.True(IdHelper.IsInitialized);
        Assert.Equal(1, ExtractWorkerId(IdHelper.NextId()));
    }

    [Fact]
    public async Task Initialization_WithoutHostEnvironment_Starts()
    {
        // 裸 ServiceCollection（单元测试 / 脚本）拿不到 IHostEnvironment，答不出「是不是生产」⇒ 只警告不拦。
        var module = new CoreServicesModule();
        using var provider = ConfigureAndBuild(module, BuildConfiguration(), hostEnvironment: null);

        await module.OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }

    [Fact]
    public void WorkerIdResolver_HostnameOrdinalOutOfRange_FailsNamingTheBitLength()
    {
        // 6 位机器码最大 63；主机名序号 99 ⇒ WorkerId 100 放不下，必须拒绝而不是溢出成别的值。
        var options = new IdGenerationOptions { WorkerIdFromHostname = true };

        var ex = Assert.Throws<ConfigurationException>(() => WorkerIdResolver.Resolve(options, "api-99"));

        Assert.Contains("WorkerIdBitLength", ex.Message);
    }

    [Fact]
    public void WorkerIdResolver_HostnameOrdinal_DerivesWorkerIdAsOrdinalPlusOne()
    {
        // StatefulSet 序号从 0 起，而雪花 WorkerId 0 不是合法机器码（生成器拒绝），故 +1。
        var options = new IdGenerationOptions { WorkerIdFromHostname = true };

        var resolution = WorkerIdResolver.Resolve(options, "api-4");

        Assert.NotNull(resolution);
        Assert.Equal(5, resolution.WorkerId);
        Assert.Equal(WorkerIdSource.Hostname, resolution.Source);
    }

    [Theory]
    [InlineData("api-2", 2)]
    [InlineData("payments-statefulset-0", 0)]
    [InlineData("web12", 12)]
    public void WorkerIdResolver_ParsesTrailingOrdinal(string machineName, int expected)
    {
        Assert.True(WorkerIdResolver.TryParseHostnameOrdinal(machineName, out var ordinal));
        Assert.Equal(expected, ordinal);
    }

    [Theory]
    [InlineData("web")]
    [InlineData("")]
    [InlineData("api-2x")]
    public void WorkerIdResolver_NoTrailingOrdinal_ReturnsFalse(string machineName)
    {
        Assert.False(WorkerIdResolver.TryParseHostnameOrdinal(machineName, out _));
    }

    [Fact]
    public void WorkerIdResolver_ExplicitWorkerIdWinsOverHostname()
    {
        var options = new IdGenerationOptions { WorkerId = 9, WorkerIdFromHostname = true };

        var resolution = WorkerIdResolver.Resolve(options, "api-3");

        Assert.NotNull(resolution);
        Assert.Equal(9, resolution.WorkerId);
        Assert.Equal(WorkerIdSource.Configuration, resolution.Source);
    }

    [Fact]
    public void WorkerIdResolver_NothingConfigured_ReturnsNull()
    {
        Assert.Null(WorkerIdResolver.Resolve(new IdGenerationOptions(), "api-3"));
    }

    [Fact]
    public void WorkerIdResolver_HostnameEnabledButNoOrdinal_ReturnsNull()
    {
        Assert.Null(WorkerIdResolver.Resolve(new IdGenerationOptions { WorkerIdFromHostname = true }, "web"));
    }

    [Theory]
    [InlineData(0, 6, 6)]     // WorkerId 0 不是合法机器码
    [InlineData(64, 6, 6)]    // 超出 6 位机器码
    [InlineData(1, 16, 8)]    // 位长之和超过 22
    [InlineData(1, 0, 6)]     // 机器码位长下限 1
    [InlineData(1, 6, 1)]     // 序列位长下限 2
    public void Validator_RejectsOutOfRangeValues(int workerId, int workerIdBitLength, int seqBitLength)
    {
        var options = new IdGenerationOptions
        {
            WorkerId = (ushort)workerId,
            WorkerIdBitLength = (byte)workerIdBitLength,
            SeqBitLength = (byte)seqBitLength,
        };

        var result = new IdGenerationOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validator_AcceptsUnconfiguredWorkerId()
    {
        // 未配置不是校验错误 —— 失败方向由环境决定（Production 启动期拦，其它环境警告）。
        var result = new IdGenerationOptionsValidator().Validate(null, new IdGenerationOptions());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void DefaultIdGenerator_WorkerIdZero_IsRejected()
    {
        // 0 不是机器码：算法此前把它替换成 DateTime.Now.Millisecond（0-999），6 位字段装不下 ⇒ 溢进时间戳位，
        // 与另一台机器码在 7 个 tick 之后的 id 空间重叠，且每次重启都换一个值。错误信息自称 [1, max]，行为要与它一致。
        var ex = Assert.Throws<InfrastructureException>(() => new DefaultIdGenerator(new IdGeneratorOptions { WorkerId = 0 }));

        Assert.Contains("WorkerId", ex.Message);
    }

    [Fact]
    public void SetIdGenerator_DefaultOptions_IsRejectedInsteadOfPickingARandomWorkerId()
    {
        // IdGeneratorOptions.WorkerId 默认 0：直接调用公开 API 的脚本 / Program.cs 是唯一绕过配置校验的入口。
        Assert.Throws<InfrastructureException>(() => IdHelper.SetIdGenerator(new IdGeneratorOptions()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(63)]
    public void DefaultIdGenerator_UsesExactlyTheConfiguredWorkerId(int workerId)
    {
        var generator = new DefaultIdGenerator(new IdGeneratorOptions { WorkerId = (ushort)workerId });

        var id = generator.NewLong();

        Assert.Equal(workerId, ExtractWorkerId(id));
    }
}
