namespace Tnzi.Signing.Permissions;

/// <summary>
/// 电子签署模块管理面的操作级权限码。
/// </summary>
/// <remarks>
/// 按 docs/coding-standards/permissions.md 在模块内自声明：加载模块就带上它的目录，
/// 不加载的宿主永远不会播种这些码。
///
/// ★ 收件人签署面（<c>signing/{token}</c>）**故意不在这里**：它是匿名的，凭一次性令牌
/// 进入。收件人是客户、对家律师、供应商 —— 给他们发一个账号才能签字，等于让电子签署
/// 比纸笔更麻烦。
/// </remarks>
public class SigningPermissions : IPermissionDefinitionProvider
{
    public void Define(IPermissionDefinitionContext context)
    {
        context.AddGroup("signing", "E-Signature");
        context.AddPermission("signing.view", "View E-Signature", parentName: "signing");
        context.AddCrudPermissions("signing.template", "Signing Templates", parentName: "signing");
        // 发起 = .create；发出/作废这类生命周期推进 = .update。
        context.AddCrudPermissions("signing.request", "Signing Requests", parentName: "signing");
    }
}
