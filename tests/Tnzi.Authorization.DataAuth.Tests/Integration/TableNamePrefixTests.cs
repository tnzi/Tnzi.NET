using Microsoft.EntityFrameworkCore.Metadata;
using Tnzi.Extensions;
using Tnzi.Modules;

namespace Tnzi.Authorization.DataAuth.Tests.Integration;

/// <summary>
/// 拆分不改表名的证据：<c>Auth_EntityInfo</c> / <c>Auth_EntityRole</c> 逐字不变，故零迁移。
/// </summary>
/// <remarks>
/// <para>
/// 前缀不是写在实体上的，而是<b>按实体所在程序集</b>到模块容器里反查那个模块的
/// <c>TableNamePrefix</c>（见 <c>src/Tnzi.EFCore/TableNamePrefixConfiguration.cs</c>）。
/// 两张表搬进新程序集之后，这条查找命中的就变成了本子模块 —— 它必须<b>逐字重声明</b>
/// 父模块的 <c>"Auth"</c>，否则表名会一声不响地退化成 <c>EntityInfo</c> / <c>EntityRole</c>，
/// 而代码、配置、日志全都看不出异样，直到某次迁移把两张有数据的表重建成空的。
/// </para>
/// <para>
/// 所以这里不是断言那个字符串等于 <c>"Auth"</c>（那只是把常量抄一遍），而是真的跑一遍
/// <c>TableNamePrefixConfiguration</c>：喂它一个装着父子两个模块的真实
/// <see cref="ModuleContainer"/>，看它给出的表名。第二个用例把子模块从容器里拿掉，
/// 复现「忘了重声明前缀」时的现场 —— 它证明第一个用例守的是一件会变的事。
/// </para>
/// </remarks>
public class TableNamePrefixTests
{
    private static IModuleContainer ContainerWith(params ITnziModule[] modules)
        => new ModuleContainer(modules
            .Select(m => (IModuleDescriptor)new ModuleDescriptor(m.GetType(), m))
            .ToList());

    private static string? TableNameFor(Type entityType, IModuleContainer container)
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity(entityType);

        var mutable = (IMutableEntityType)modelBuilder.Model.FindEntityType(entityType)!;
        new TableNamePrefixConfiguration(container).Configure(modelBuilder, mutable);

        return modelBuilder.Model.FindEntityType(entityType)!.GetTableName();
    }

    [Theory]
    [InlineData(typeof(EntityInfo), "Auth_EntityInfo")]
    [InlineData(typeof(EntityRole), "Auth_EntityRole")]
    public void MovedEntities_KeepTheParentsTablePrefix(Type entityType, string expected)
    {
        var container = ContainerWith(new AuthorizationModule(), new AuthorizationDataAuthModule());

        TableNameFor(entityType, container).ShouldBe(expected);
    }

    /// <summary>
    /// 子模块不在容器里（等价于它没有重声明前缀）时，表名<b>丢掉前缀</b>。
    /// 这条不是在守一个期望的行为，而是在证明上一条守的东西真的会变。
    /// </summary>
    [Theory]
    [InlineData(typeof(EntityInfo), "EntityInfo")]
    [InlineData(typeof(EntityRole), "EntityRole")]
    public void WithoutTheChildModule_ThePrefixIsSilentlyLost(Type entityType, string unprefixed)
    {
        var parentOnly = ContainerWith(new AuthorizationModule());

        TableNameFor(entityType, parentOnly).ShouldBe(unprefixed);
    }

    /// <summary>父子两个模块必须给出同一个前缀字符串 —— 拆的是程序集不是 schema。</summary>
    [Fact]
    public void ChildDeclaresTheSamePrefixAsTheParent()
    {
        new AuthorizationDataAuthModule().TableNamePrefix
            .ShouldBe(new AuthorizationModule().TableNamePrefix);
    }

    /// <summary>前缀查找按<b>实体所在程序集</b>命中，所以两张表必须与子模块类同程序集。</summary>
    [Theory]
    [InlineData(typeof(EntityInfo))]
    [InlineData(typeof(EntityRole))]
    public void MovedEntities_LiveInTheChildAssembly(Type entityType)
    {
        entityType.Assembly.ShouldBe(typeof(AuthorizationDataAuthModule).Assembly);
    }
}
