namespace Tnzi.Identity.OpenIdConnect.Permissions;

/// <summary>
/// OpenID Connect 提供方管理面的权限码（6 个，全部 Technical）。
/// </summary>
/// <remarks>
/// 随模块走：不当身份提供方的宿主不加载本模块，也就不 seed 这些码。父组复用核心声明的 <c>identity</c>。
/// <para>
/// ★ <b>Technical</b>：登记一个客户端就是让一个外部应用能凭平台账号登录，撤销授权则让用户在那个应用里掉线 ——
/// 这是信任边界的配置，不是日常业务面，授权界面要给它挂警示标记。
/// </para>
/// <para>
/// 授权（grant）只有 view 与 delete：授权由用户在登录或同意时产生，管理员没有「代人同意」的场景。
/// </para>
/// </remarks>
public class IdentityOpenIdConnectPermissions : IPermissionDefinitionProvider
{
    /// <inheritdoc />
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddCrudPermissions(
            OidcConstants.ClientPermissionPrefix,
            "OpenID Connect Clients",
            parentName: "identity",
            category: PermissionCategory.Technical);

        context.AddCrudPermissions(
            OidcConstants.GrantPermissionPrefix,
            "OpenID Connect Grants",
            parentName: "identity",
            category: PermissionCategory.Technical,
            actions: CrudActions.View | CrudActions.Delete);
    }
}
