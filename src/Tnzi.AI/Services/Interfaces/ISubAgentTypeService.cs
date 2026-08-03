namespace Tnzi.AI.Services;

/// <summary>
/// 子 Agent 类型定义的管理端服务（CRUD）。
/// </summary>
/// <remarks>
/// 每次写入后整表重载 <c>ISubAgentRegistry</c>：注册表按名称（大小写不敏感）单键存放，
/// 删除一条覆盖了内置名（general-purpose / bash / researcher）的 DB 定义时，
/// 逐条 Unregister 会把内置定义一并摘掉；重载才能重新注册内置 + 剩余启用行。
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
