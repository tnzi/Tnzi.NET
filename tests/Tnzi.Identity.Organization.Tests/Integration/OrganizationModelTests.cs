namespace Tnzi.Identity.Organization.Tests.Integration;

using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 两个包都加载时，真实 EF 模型与拆分前逐字相同：表名、外键、删除行为、约束名。
/// </summary>
/// <remarks>
/// <see cref="TableNamingTests"/> 问的是"前缀解析器答什么"，这里问的是"模型建出来是什么" ——
/// 前者能在解析器之外的地方失效（比如有人给实体补了一个显式 <c>ToTable</c>），后者不会。
/// </remarks>
public class OrganizationModelTests : OrganizationIntegrationTestBase
{
    [Fact]
    public void OrganizationTable_KeepsTheParentsPrefix()
    {
        DbContext.Model.FindEntityType(typeof(Organization))!.GetTableName()
            .ShouldBe("Identity_Organization");
    }

    /// <summary>对照组：核心实体的表名同样不变（前缀机制在工作，不是碰巧写死对了）。</summary>
    [Fact]
    public void UserTable_IsUnchanged()
    {
        DbContext.Model.FindEntityType(typeof(User))!.GetTableName().ShouldBe("Identity_User");
    }

    /// <summary>
    /// 外键从主体侧（本包的 <c>OrganizationConfiguration</c>）声明，得到的关系与拆分前一模一样：
    /// 落在 <c>User.OrganizationId</c> 上、指向 Organization、删除时置空、约束名由 EF 按默认规则算。
    /// </summary>
    /// <remarks>
    /// 约束名刻意<b>不</b>在配置里写死 —— 两侧声明算出来的默认名一致，手写反而会在表名不是
    /// 默认值的宿主上凭空造出一次 rename。这条断言钉住的是"默认名仍然是那一个"。
    /// </remarks>
    [Fact]
    public void ForeignKey_FromUserToOrganization_IsUnchanged()
    {
        var user = DbContext.Model.FindEntityType(typeof(User))!;

        var fk = user.GetForeignKeys().SingleOrDefault(f =>
            f.PrincipalEntityType.ClrType == typeof(Organization));

        fk.ShouldNotBeNull("User 上没有指向 Organization 的外键 —— 主体侧那条 HasMany<User>() 丢了");
        fk!.Properties.Select(p => p.Name).ShouldBe(new[] { nameof(User.OrganizationId) });
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.SetNull);
        fk.GetConstraintName().ShouldBe("FK_Identity_User_Identity_Organization_OrganizationId");
    }

    /// <summary>
    /// 核心的 <c>User</c> 不得再有指向 Organization 的**导航属性** —— 有它，这个包就拆不出去。
    /// </summary>
    [Fact]
    public void User_HasNoNavigationToOrganization()
    {
        typeof(User).GetProperties()
            .Any(p => p.PropertyType == typeof(Organization))
            .ShouldBeFalse("User 又长出了 Organization 导航属性：父模块从此引用子模块的实体");

        DbContext.Model.FindEntityType(typeof(User))!.GetNavigations()
            .Any(n => n.TargetEntityType.ClrType == typeof(Organization))
            .ShouldBeFalse();
    }

    /// <summary>
    /// <c>User.OrganizationId</c> 的索引留在核心：加不加载本包，User 表的列形状都必须一样。
    /// </summary>
    [Fact]
    public void UserOrganizationIdIndex_StaysInTheCore()
    {
        DbContext.Model.FindEntityType(typeof(User))!.GetIndexes()
            .Any(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(User.OrganizationId))
            .ShouldBeTrue();
    }

    /// <summary>组织层级往返（自引用父子关系）。</summary>
    [Fact]
    public async Task OrganizationHierarchy_RoundTrips()
    {
        var root = new Organization { Id = Guid.NewGuid(), Name = "Root Organization", Code = "ROOT", CreationTime = DateTime.UtcNow };
        var child = new Organization { Id = Guid.NewGuid(), Name = "Child Organization", Code = "CHILD", ParentId = root.Id, CreationTime = DateTime.UtcNow };

        DbContext.Set<Organization>().Add(root);
        DbContext.Set<Organization>().Add(child);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var savedRoot = await DbContext.Set<Organization>()
            .Include(o => o.Children)
            .FirstOrDefaultAsync(o => o.Code == "ROOT");

        savedRoot.ShouldNotBeNull();
        savedRoot!.Children.Count.ShouldBe(1);
        savedRoot.Children.First().Name.ShouldBe("Child Organization");
    }
}
