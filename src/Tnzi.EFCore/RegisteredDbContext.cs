namespace Tnzi.EFCore;

/// <summary>
/// <c>AddTnziDbContext</c> 登记的一个 DbContext 类型。<c>IEnumerable&lt;RegisteredDbContext&gt;</c> 即全部登记。
/// </summary>
/// <remarks>
/// ★ 存在的理由：<see cref="UnitOfWorkManager"/> 此前只能靠「重新绑定 <c>Database</c> 配置读
/// <c>DbContextType</c>」与「<c>IEntityManager</c> 按实体配置反推」两条途径发现上下文，
/// 而文档明确支持的「只写 <c>Name</c>、启动期按名字自动发现」形态两条都取不到主上下文 ——
/// 提交循环一个工作单元都不建，事务内缓冲的写入随作用域释放静默消失、接口 200。
/// 所有 DbContext 都经过 <c>AddTnziDbContext</c> 这个漏斗（自动发现模式也是经它注册），
/// 在漏斗里登记类型，发现就不再依赖配置的写法。<c>EFCoreModule</c> 的启动核对也读它。
/// </remarks>
public sealed class RegisteredDbContext
{
    public RegisteredDbContext(Type dbContextType, bool isPrimary)
    {
        DbContextType = Check.NotNull(dbContextType);
        IsPrimary = isPrimary;
    }

    /// <summary>DbContext 的 CLR 类型。</summary>
    public Type DbContextType { get; }

    /// <summary>是否以主上下文登记（<c>AddTnziDbContext(isPrimary: true)</c>）。</summary>
    public bool IsPrimary { get; }
}
