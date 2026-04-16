namespace Tnzi.AI.Mcp.Services.Interfaces;

/// <summary>
/// MCP Server 注册表服务接口 — 管理外部 MCP Server 客户端注册条目的 CRUD
/// </summary>
/// <remarks>
/// 该服务提供数据库驱动的 MCP Server 注册管理，与 <see cref="Tnzi.AI.Mcp.Options.McpServerOptions"/>
/// 描述的 server-hosting 配置完全无关 — 后者用于把 Tnzi 自身暴露为 MCP Server。
///
/// 本阶段（Phase 5 backend prereq）只提供 CRUD 表面；MCP 运行时连接路径目前不读取本表，
/// 实体 → 运行时绑定为后续工作。<c>AuthToken</c> 通过 <c>IDataProtectionProvider</c> 加密，
/// 永远不会通过 DTO 暴露明文或密文。
/// </remarks>
public interface IMcpServerRegistryService
{
    /// <summary>分页查询 MCP Server 注册列表</summary>
    Task<Result<IPagedList<McpServerRegistrationDto>>> GetPagedListAsync(McpServerRegistrationQueryDto query, CancellationToken ct = default);

    /// <summary>按 ID 获取 MCP Server 注册</summary>
    Task<Result<McpServerRegistrationDto>> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>创建 MCP Server 注册</summary>
    Task<Result<McpServerRegistrationDto>> CreateAsync(CreateMcpServerRegistrationDto dto, CancellationToken ct = default);

    /// <summary>更新 MCP Server 注册 — AuthToken 为 null 时保留现有密文</summary>
    Task<Result<McpServerRegistrationDto>> UpdateAsync(Guid id, UpdateMcpServerRegistrationDto dto, CancellationToken ct = default);

    /// <summary>软删除 MCP Server 注册</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>测试 MCP Server 连接（轻量探针：校验 IsEnabled + URL 解析 + 凭证解密）</summary>
    Task<Result<McpServerTestResultDto>> TestConnectionAsync(Guid id, CancellationToken ct = default);
}
