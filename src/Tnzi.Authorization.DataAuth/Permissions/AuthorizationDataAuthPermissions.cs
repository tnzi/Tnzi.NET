namespace Tnzi.Authorization.DataAuth.Permissions;

/// <summary>
/// 行级数据授权 admin 面的操作级权限码（4 个，<c>authorization.entityRole.*</c>）。
/// </summary>
/// <remarks>
/// 随模块走：不加载本子模块的宿主永远不会 seed 这四个码，权限矩阵里也不会多出一块
/// 点进去就 404 的功能面。码串与拆分前<b>逐字相同</b>，既有角色授权行不需要迁移。
/// <br/><br/>
/// 本 provider <b>不</b> <c>AddGroup</c>：父组 <c>authorization</c> 由
/// <c>AuthorizationPermissions</c> 声明，两块在授权界面里合并成同一棵树。
/// 子模块重复声明父组不是「幂等所以无害」—— 目录门禁要求每个 provider 只声明自己的组，
/// 而这个组不是本模块的。
/// </remarks>
public class AuthorizationDataAuthPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        // 类别不显式指定：随 authorization 组的 defaultCategory（Technical）——
        // 「谁能改数据范围」等于「谁能决定别人看得见哪几行」，是安全面不是业务面。
        context.AddCrudPermissions("authorization.entityRole", "Entity Roles", parentName: "authorization");
    }
}
