using Tnzi.Extensions;
using Tnzi.Modules;
using Tnzi.Storage.Entities.Configs;

namespace Tnzi.Storage.Workspace.Tests;

/// <summary>
/// 拆分的<b>唯一不可逆</b>约束：五张表搬了程序集，表名必须一个字符都不变。
/// </summary>
/// <remarks>
/// <para>
/// 前缀不是写在实体上的，而是 <c>TableNamePrefixConfiguration</c> 拿<b>实体所在程序集</b>
/// 回模块容器里查出来的。实体换了程序集，就必须由新程序集的模块把同一个前缀字符串再声明一遍。
/// 漏掉那一行 <c>TableNamePrefix</c> 的表现是：编译通过、所有业务测试照绿、
/// 而迁移里五张表悄悄丢掉 <c>Storage_</c> 前缀变成五张空的新表。
/// </para>
/// <para>
/// 所以这里不是断言「两个模块的字符串相等」（那只是重复了实现），而是<b>真的跑一遍前缀解析</b>，
/// 把最终表名逐个钉死成拆分前的字面值。
/// </para>
/// </remarks>
public class TableNamingTests
{
    /// <summary>
    /// 一个只用来把模型建出来的上下文：装上七个配置，再按生产路径跑一遍前缀解析。
    /// </summary>
    private sealed class PrefixProbeDbContext(IModuleContainer container) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlite("Data Source=:memory:");

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new FileRecordConfiguration());
            modelBuilder.ApplyConfiguration(new FileReferenceConfiguration());
            modelBuilder.ApplyConfiguration(new FileFolderConfiguration());
            modelBuilder.ApplyConfiguration(new FileShareConfiguration());
            modelBuilder.ApplyConfiguration(new FileVersionConfiguration());
            modelBuilder.ApplyConfiguration(new FileUploadSessionConfiguration());
            modelBuilder.ApplyConfiguration(new FileChunkConfiguration());

            // 与 EntityRegistrationHelper.ApplyBatchConfigurations 同一条路径。
            var prefixer = new TableNamePrefixConfiguration(container);
            foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
            {
                prefixer.Configure(modelBuilder, entityType);
            }
        }
    }

    /// <summary>父子两个模块都在容器里，正是宿主加载了工作区包时的样子。</summary>
    private static IModuleContainer BothModules() => new ModuleContainer(
    [
        new ModuleDescriptor(typeof(StorageModule), new StorageModule()),
        new ModuleDescriptor(typeof(StorageWorkspaceModule), new StorageWorkspaceModule()),
    ]);

    [Theory]
    // 搬走的五张表
    [InlineData(typeof(FileFolder), "Storage_FileFolder")]
    [InlineData(typeof(FileShare), "Storage_Share")]
    [InlineData(typeof(FileVersion), "Storage_Version")]
    [InlineData(typeof(FileUploadSession), "Storage_UploadSession")]
    [InlineData(typeof(FileChunk), "Storage_Chunk")]
    // 留在父模块的两张，作为对照：前缀解析对两侧是同一套
    [InlineData(typeof(FileRecord), "Storage_Record")]
    [InlineData(typeof(FileReference), "Storage_Reference")]
    public void MovedTables_KeepTheirExactNames(Type entityType, string expected)
    {
        using var db = new PrefixProbeDbContext(BothModules());

        var actual = db.Model.FindEntityType(entityType)!.GetTableName();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChildModule_DeclaresTheParentsPrefixVerbatim()
    {
        // 这一条是上面那批的「为什么」：前缀按程序集查模块，所以子模块必须自己再声明一遍。
        Assert.Equal(new StorageModule().TableNamePrefix, new StorageWorkspaceModule().TableNamePrefix);
        Assert.Equal("Storage", new StorageWorkspaceModule().TableNamePrefix);
    }

    [Fact]
    public void ChildModule_OwnsTables_SoItMustBeAnApplicationModule()
    {
        // TnziCustomModule 没有 TableNamePrefix，用它会让五张表全部丢前缀；
        // 而 GET /admin/shell/modules 也只报告 TnziApplicationModule，前端的 moduleGate 会永远看不到它。
        Assert.IsAssignableFrom<TnziApplicationModule>(new StorageWorkspaceModule());
    }

    [Fact]
    public void EveryMovedConfiguration_PinsItsTableNameExplicitly()
    {
        // 显式 ToTable 把表名从「CLR 类名恰好没被改过」这件事上解绑：
        // 少了它，一次重命名类就会静默重命名一张有数据的表，而没有任何测试会红。
        using var db = new PrefixProbeDbContext(BothModules());

        foreach (var clr in new[]
                 {
                     typeof(FileFolder), typeof(FileShare), typeof(FileVersion),
                     typeof(FileUploadSession), typeof(FileChunk),
                 })
        {
            var table = db.Model.FindEntityType(clr)!.GetTableName();
            Assert.StartsWith("Storage_", table!, StringComparison.Ordinal);
        }
    }
}
