namespace Tnzi.Authorization.Options;

/// <summary>
/// 权限码「不再被任何 provider 声明」时，<c>PermissionDbSeeder</c> 对已有数据行的处置方式。
/// </summary>
/// <remarks>
/// 触发这件事的最常见原因不是「产品删掉了这个权限」，而是<b>这个部署没有加载那个模块</b>。
/// 权限码跟着 <see cref="Tnzi.Security.Authorization.IPermissionDefinitionProvider"/> 类走，
/// 所以把一个模块拆成子模块之后，升级了框架却没有补上子模块 <c>[DependsOn]</c> 的宿主，
/// 下一次启动就不再声明那些码。
/// </remarks>
public enum PermissionRetirementMode
{
    /// <summary>
    /// 默认。把数据行标记为 <c>IsRetired</c>，<b>保留行与全部授权</b>。
    /// 退役期间它不参与权限解析、也不出现在分配矩阵里；
    /// 该码重新被声明时标记自动清除，原有角色/用户授权原样恢复。
    /// </summary>
    Disable = 0,

    /// <summary>
    /// 删除数据行及其 <c>RoleFunction</c> 授权与 <c>UserFunction</c> 用户直授（allow / deny 两种行）。
    /// ⚠ 不可逆：软删过滤器会让重新声明时插入一条新 id 的行，旧授权永远接不回来。
    /// 只有在确知「这个权限确实从产品里移除了」且希望清库时才选它。
    /// 用户直授必须显式删：<c>ModuleFunction</c> 是软删，<c>UserFunction.FunctionId</c> 上的级联外键
    /// 永远不会触发，不删则 allow / deny 行继续指向墓碑，重新声明后永久不可见也无法清理。
    /// </summary>
    Delete = 1,

    /// <summary>
    /// 完全不处理，未声明的行保持原样（仍可被分配）。
    /// 适合多个部署共用一个数据库、各自加载不同模块子集的场景。
    /// </summary>
    Off = 2,
}
