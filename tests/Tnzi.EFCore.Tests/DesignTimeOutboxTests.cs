using Microsoft.EntityFrameworkCore.Infrastructure;
using Tnzi.EFCore.Outbox;
using Tnzi.EFCore.Outbox.Configs;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 设计期（<c>dotnet ef</c>）建模时 <c>OutboxMessage</c> 是否随 <c>EFCore:Outbox:Enabled</c> 进模型。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是「开了 Outbox，迁移里却永远没有那张表」：控制建模的静态开关此前只由
/// <c>EFCoreModule.ConfigureServicesAsync</c> 写，而设计期工厂不跑模块生命周期，
/// 开关又是 internal，手写工厂的消费方连设都设不了。于是运行期模型含该实体、迁移不含 ——
/// 首条集成事件 INSERT 打到一张不存在的表，承载它的业务事务整笔失败。
/// </para>
/// <para>
/// 这些用例改进程级静态，归入不并行的集合；每条都在 finally 里把开关放回去。
/// </para>
/// </remarks>
[Collection(DesignTimeStaticsCollection.Name)]
public class DesignTimeOutboxTests
{
    /// <summary>
    /// 开关本身：打开时 <see cref="OutboxMessage"/> 进模型，关闭时不进。
    /// 不经模块图，按设计期工厂的构造形状直接建上下文。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutboxSwitch_DecidesWhetherOutboxMessageIsInTheModel(bool enabled)
    {
        var original = OutboxMessageConfiguration.OutboxEnabled;
        try
        {
            OutboxMessageConfiguration.OutboxEnabled = enabled;

            var options = new DbContextOptionsBuilder<MultiTenancyProbeDbContext>()
                .UseSqlite("DataSource=:memory:")
                .ReplaceService<IModelCacheKeyFactory, NoModelCacheKeyFactory>()
                .Options;
            using var context = new MultiTenancyProbeDbContext(options, new DesignTimeCurrentUser());

            Assert.Equal(enabled, context.Model.FindEntityType(typeof(OutboxMessage)) != null);
        }
        finally
        {
            OutboxMessageConfiguration.OutboxEnabled = original;
        }
    }

    /// <summary>
    /// 端到端：<see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 从 appsettings 读到开关，
    /// 建出的上下文含 <see cref="OutboxMessage"/>。修复前工厂根本不读那一节。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DesignTimeFactory_ReadsTheOutboxSwitchFromAppSettings(bool enabled)
    {
        var original = OutboxMessageConfiguration.OutboxEnabled;
        try
        {
            // 先把静态放到与配置相反的值上，证明是工厂改的它。
            OutboxMessageConfiguration.OutboxEnabled = !enabled;

            using var settings = new TemporaryAppSettings(new
            {
                EFCore = new { Outbox = new { Enabled = enabled } },
                Database = new
                {
                    DbContexts = new[]
                    {
                        new { Name = "Default", ConnectionString = "Data Source=:memory:", Provider = "Sqlite" },
                    },
                },
            });

            using var context = new ProbeDesignTimeFactory(settings.ConfigurationDirectory).CreateDbContext([]);

            Assert.Equal(enabled, OutboxMessageConfiguration.OutboxEnabled);
            Assert.Equal(enabled, context.Model.FindEntityType(typeof(OutboxMessage)) != null);
        }
        finally
        {
            OutboxMessageConfiguration.OutboxEnabled = original;
        }
    }
}
