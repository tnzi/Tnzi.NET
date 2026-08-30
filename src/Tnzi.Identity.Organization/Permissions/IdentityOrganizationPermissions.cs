namespace Tnzi.Identity.Organization.Permissions;

/// <summary>
/// 组织架构 admin 面的操作级权限码（4 个）。
/// </summary>
/// <remarks>
/// 码串与拆分前**逐字节相同**（<c>organization.view</c> / <c>.create</c> / <c>.update</c> /
/// <c>.delete</c>）：权限码是持久化契约，已授出去的角色行按码串匹配，改名等于把所有人的
/// 授权静默清空；管理端路由的 <c>meta.permission</c> 也逐字引用它们。
///
/// 父组仍是核心 <see cref="Tnzi.Identity.Permissions.IdentityPermissions"/> 声明的
/// <c>identity</c>，本模块**不再 AddGroup 一次** —— 沿 <c>FinanceBankingPermissions</c> /
/// <c>FinanceOffersPermissions</c> 的做法：组由父模块声明，子模块只往里挂码，
/// 两块在授权界面里合并成一棵树。（<c>AddGroup</c> 是 first-wins 的，重复声明本身不报错，
/// 但目录 pact 要求每个 provider 只声明属于自己的组，重复声明会让组的显示名
/// 取决于模块加载顺序。）
///
/// 随模块走：没有组织架构的宿主（用户是自然人、或主体是服务的无头 API）永远不会 seed
/// 这 4 个码，权限矩阵里也不会多出一块永远不授予的功能面 —— 那比少四行更糟，
/// 管理员会以为授了权就能用。
/// </remarks>
public class IdentityOrganizationPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddCrudPermissions("organization", "Organizations", parentName: "identity");
    }
}
