namespace Tnzi.Authorization.Tests.Options;

/// <summary>
/// <see cref="PermissionScopeRegistry"/> 的判定顺序：部署覆盖 → 组上声明 → 父组 → 声明它的程序集。
/// 本测试程序集名以 <c>Tnzi.</c> 打头，所以未声明的组在这里默认是平台。
/// </summary>
public class PermissionScopeRegistryTests
{
    private sealed class Provider(Action<IPermissionDefinitionContext> define) : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context) => define(context);
    }

    [Fact]
    public void UndeclaredScope_FollowsTheDeclaringAssembly()
    {
        var registry = PermissionScopeRegistry.Build([new Provider(c =>
        {
            c.AddGroup("ops", "Ops");
            c.AddPermission("ops.view", "View", parentName: "ops");
        })]);

        registry.GetGroupScope("ops").ShouldBe(PermissionScope.Platform);
        registry.GetScope("OPS.VIEW").ShouldBe(PermissionScope.Platform, "codes are case-insensitive");
    }

    [Theory]
    [InlineData("Tnzi", true)]
    [InlineData("Tnzi.Finance", true)]
    [InlineData("TnziApp", false)]
    [InlineData("App.Core", false)]
    [InlineData(null, false)]
    public void FrameworkAssembly_IsDecidedByThePrefix(string? name, bool expected)
        => PermissionScopeRegistry.IsFrameworkAssembly(name).ShouldBe(expected);

    [Fact]
    public void DeclaredScope_WinsOverTheAssemblyDefault_AndCodesFollowTheNearestGroupThroughParentCodes()
    {
        var registry = PermissionScopeRegistry.Build([new Provider(c =>
        {
            c.AddGroup("crm", "CRM").Scope = PermissionScope.Application;
            c.AddPermission("crm.view", "View", parentName: "crm");
            c.AddPermission("crm.export", "Export", parentName: "crm.view");
        })]);

        registry.GetScope("crm.view").ShouldBe(PermissionScope.Application);
        registry.GetScope("crm.export").ShouldBe(PermissionScope.Application);
    }

    [Fact]
    public void SharedGroup_TakesTheScopeWhicheverContributorDeclaresIt_RegardlessOfOrder()
    {
        var declaring = new Provider(c => c.AddGroup("books", "Books").Scope = PermissionScope.Application);
        var contributing = new Provider(c =>
        {
            c.AddGroup("books", "Books");
            c.AddPermission("books.bank.view", "Bank", parentName: "books");
        });

        PermissionScopeRegistry.Build([declaring, contributing]).GetScope("books.bank.view").ShouldBe(PermissionScope.Application);
        PermissionScopeRegistry.Build([contributing, declaring]).GetScope("books.bank.view").ShouldBe(PermissionScope.Application);
    }

    [Fact]
    public void ChildGroup_WithoutAScope_InheritsItsParent()
    {
        var registry = PermissionScopeRegistry.Build([new Provider(c =>
        {
            c.AddGroup("books", "Books").Scope = PermissionScope.Application;
            c.AddGroup("books.tax", "Tax", parentName: "books");
            c.AddPermission("books.tax.view", "View", parentName: "books.tax");
        })]);

        registry.GetGroupScope("books.tax").ShouldBe(PermissionScope.Application);
        registry.GetScope("books.tax.view").ShouldBe(PermissionScope.Application);
    }

    [Fact]
    public void DeploymentOverride_WinsOverTheDeclaration_CaseInsensitively()
    {
        var registry = PermissionScopeRegistry.Build(
            [new Provider(c => c.AddGroup("storage", "Storage"))],
            new Dictionary<string, PermissionScope> { ["STORAGE"] = PermissionScope.Application });

        registry.GetGroupScope("storage").ShouldBe(PermissionScope.Application);
    }

    [Fact]
    public void UndeclaredCode_HasNoScope_SoTheNarrowedBypassFailsClosed()
    {
        var registry = PermissionScopeRegistry.Build([new Provider(c => c.AddGroup("ops", "Ops"))]);
        var policy = SuperAdminBypassPolicy.Create(Tnzi.Authorization.Options.SuperAdminBypassScope.Platform, registry);

        registry.GetScope("nobody.declares.this").ShouldBeNull();
        policy.Covers("nobody.declares.this").ShouldBeFalse();
        SuperAdminBypassPolicy.Create(Tnzi.Authorization.Options.SuperAdminBypassScope.All, registry)
            .Covers("nobody.declares.this").ShouldBeTrue();
    }

    [Fact]
    public void ProviderThatThrows_KeepsWhatItDeclaredBeforeThrowing()
    {
        var registry = PermissionScopeRegistry.Build([new Provider(c =>
        {
            c.AddGroup("ops", "Ops");
            c.AddPermission("ops.view", "View", parentName: "ops");
            throw new InvalidOperationException("boom");
        })]);

        registry.GetScope("ops.view").ShouldBe(PermissionScope.Platform);
    }
}
