namespace Tnzi.AI.Services;

/// <summary>
/// 子 Agent 类型定义的管理端服务（CRUD）。
/// </summary>
/// <remarks>
/// 每次写入后整桶重载 <c>ISubAgentRegistry</c> 里当前租户的桶：注册表按名称（大小写不敏感）单键存放，
/// 删除一条覆盖了内置名（general-purpose / bash / researcher）的 DB 定义时，
/// 逐条 Unregister 会把内置定义一并摘掉；重载才能重新注册内置 + 剩余启用行。
/// 只重载当前租户的桶（<c>LoadTenantFromStoreAsync</c>）：注册表是进程级单例，仓储受本作用域的租户过滤，
/// 整表重载会清掉其它租户的类型、并把本租户的定义暴露给全体。
/// 重载前先经工作单元 flush 本次写入：开着全局工作单元时不 flush，重载读到的是请求之前的数据。
/// 同一租户内类型名（大小写不敏感）重复时创建 / 更新返回 <b>409</b> <c>AI_SUB_AGENT_TYPE_ALREADY_EXISTS</c>。
/// </remarks>
public interface ISubAgentTypeService
{
    /// <summary>获取全部子 Agent 类型定义（按名称升序）</summary>
    Task<Result<List<SubAgentTypeDto>>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>创建定义</summary>
    Task<Result<SubAgentTypeDto>> CreateAsync(SubAgentTypeInputDto input, CancellationToken cancellationToken = default);

    /// <summary>更新定义</summary>
    Task<Result<SubAgentTypeDto>> UpdateAsync(Guid id, SubAgentTypeInputDto input, CancellationToken cancellationToken = default);

    /// <summary>删除定义</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
