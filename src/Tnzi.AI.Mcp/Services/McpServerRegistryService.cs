using Microsoft.AspNetCore.DataProtection;
using Tnzi.AI.Mcp.Services.Interfaces;

namespace Tnzi.AI.Mcp.Services;

/// <summary>
/// MCP Server 注册表服务实现 — 数据库驱动的 CRUD + 凭证加密
/// </summary>
/// <remarks>
/// AuthToken 加密策略：
///   使用 ASP.NET Core 标准的 <c>IDataProtectionProvider</c>，purpose 字符串为
///   "Tnzi.AI.Mcp.ServerCredential"。明文凭证永远不会出现在 DTO 中（仅暴露
///   <c>HasAuthToken</c> 布尔标志），密文也不会暴露。<c>IDataProtectionProvider</c>
///   由 AIModule.AddDataProtection() 注册，AIMcpModule 通过 [DependsOn(typeof(AIModule))]
///   自动获取。
///
/// TestConnectionAsync 探针策略：
///   默认采用 "shallow probe" — 仅校验记录基础信息（IsEnabled、ServerUrl 可解析为
///   合法 URI）并在密文存在时校验解密成功。不发起真实 MCP 连接以避免引入完整的
///   MCP 客户端栈（且远端服务可能不可达，会引入测试时延和不稳定）。
///
/// 重要：本服务的 CRUD 表面是独立的，MCP 运行时连接路径目前仍读取 McpServerOptions
/// 配置；实体 → 运行时绑定为后续工作。
/// </remarks>
public class McpServerRegistryService : ApplicationService, IMcpServerRegistryService
{
    private const string ProtectorPurpose = "Tnzi.AI.Mcp.ServerCredential";

    private readonly IRepository<McpServerRegistration, Guid> _repository;
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public McpServerRegistryService(
        IRepository<McpServerRegistration, Guid> repository,
        IDataProtectionProvider dataProtectionProvider,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _dataProtectionProvider = Check.NotNull(dataProtectionProvider);
    }

    private IDataProtector GetProtector() => _dataProtectionProvider.CreateProtector(ProtectorPurpose);

    private static McpServerRegistrationDto ToDto(McpServerRegistration entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ServerUrl = entity.ServerUrl,
        Transport = entity.Transport,
        Command = entity.Command,
        Arguments = entity.Arguments,
        AuthType = entity.AuthType,
        Priority = entity.Priority,
        IsEnabled = entity.IsEnabled,
        Description = entity.Description,
        Tags = entity.Tags,
        HasAuthToken = !string.IsNullOrEmpty(entity.AuthTokenEncrypted),
        CreationTime = entity.CreationTime,
        LastModificationTime = entity.LastModificationTime
    };

    /// <inheritdoc />
    public async Task<Result<IPagedList<McpServerRegistrationDto>>> GetPagedListAsync(McpServerRegistrationQueryDto query, CancellationToken ct = default)
    {
        Check.NotNull(query);

        try
        {
            var keyword = query.Keyword?.Trim().ToLower();
            var transport = query.Transport?.Trim();

            var queryable = _repository.AsQueryable()
                .WhereIf(p => p.Transport == transport!, !string.IsNullOrEmpty(transport))
                .WhereIf(p => p.IsEnabled == query.IsEnabled!.Value, query.IsEnabled.HasValue)
                .WhereIf(
                    p => p.Name.ToLower().Contains(keyword!) || (p.Description != null && p.Description.ToLower().Contains(keyword!)),
                    !string.IsNullOrEmpty(keyword))
                .OrderByDescending(p => p.Priority)
                .ThenByDescending(p => p.CreationTime);

            var totalCount = await queryable.CountAsync(ct);
            var items = await queryable
                .Skip((query.PageIndex - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(ct);

            var dtoItems = items.Select(ToDto).ToList();
            var paged = new PagedList<McpServerRegistrationDto>(dtoItems, query.PageIndex, query.PageSize, totalCount);
            return Ok<IPagedList<McpServerRegistrationDto>>(paged);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error querying MCP server registrations");
            return Fail<IPagedList<McpServerRegistrationDto>>("Failed to query MCP server registrations", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<McpServerRegistrationDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var entity = await _repository.AsQueryable().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<McpServerRegistrationDto>("MCP server registration not found", 404, ErrorCodes.McpServerRegistrationNotFound);
            }
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error getting MCP server registration {Id}", id);
            return Fail<McpServerRegistrationDto>("Failed to get MCP server registration", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<McpServerRegistrationDto>> CreateAsync(CreateMcpServerRegistrationDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);

        try
        {
            var name = dto.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return Fail<McpServerRegistrationDto>("Registration name is required", 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }
            if (string.IsNullOrWhiteSpace(dto.ServerUrl))
            {
                return Fail<McpServerRegistrationDto>("Server URL is required", 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }
            if (string.IsNullOrWhiteSpace(dto.Transport))
            {
                return Fail<McpServerRegistrationDto>("Transport is required", 400, ErrorCodes.McpServerRegistrationOperationFailed);
            }

            var exists = await _repository.AsQueryable().AnyAsync(p => p.Name == name, ct);
            if (exists)
            {
                return Fail<McpServerRegistrationDto>($"MCP server registration with name '{name}' already exists", 409, ErrorCodes.McpServerRegistrationAlreadyExists);
            }

            var entity = new McpServerRegistration
            {
                Name = name,
                ServerUrl = dto.ServerUrl.Trim(),
                Transport = dto.Transport.Trim(),
                Command = dto.Command,
                Arguments = dto.Arguments,
                AuthType = dto.AuthType,
                Priority = dto.Priority,
                IsEnabled = dto.IsEnabled,
                Description = dto.Description,
                Tags = dto.Tags,
                AuthTokenEncrypted = string.IsNullOrEmpty(dto.AuthToken) ? null : GetProtector().Protect(dto.AuthToken)
            };

            await _repository.InsertAsync(entity);
            LogInformation("Created MCP server registration {Name} (transport {Transport})", entity.Name, entity.Transport);
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error creating MCP server registration");
            return Fail<McpServerRegistrationDto>("Failed to create MCP server registration", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<McpServerRegistrationDto>> UpdateAsync(Guid id, UpdateMcpServerRegistrationDto dto, CancellationToken ct = default)
    {
        Check.NotNull(dto);

        try
        {
            var entity = await _repository.AsQueryable(withTracking: true).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<McpServerRegistrationDto>("MCP server registration not found", 404, ErrorCodes.McpServerRegistrationNotFound);
            }

            if (!string.IsNullOrWhiteSpace(dto.Name) && dto.Name.Trim() != entity.Name)
            {
                var newName = dto.Name.Trim();
                var conflict = await _repository.AsQueryable().AnyAsync(p => p.Id != id && p.Name == newName, ct);
                if (conflict)
                {
                    return Fail<McpServerRegistrationDto>($"MCP server registration with name '{newName}' already exists", 409, ErrorCodes.McpServerRegistrationAlreadyExists);
                }
                entity.Name = newName;
            }

            if (!string.IsNullOrWhiteSpace(dto.ServerUrl))
                entity.ServerUrl = dto.ServerUrl.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Transport))
                entity.Transport = dto.Transport.Trim();
            if (dto.Command != null)
                entity.Command = dto.Command;
            if (dto.Arguments != null)
                entity.Arguments = dto.Arguments;
            if (dto.AuthType != null)
                entity.AuthType = dto.AuthType;
            if (dto.Priority.HasValue)
                entity.Priority = dto.Priority.Value;
            if (dto.IsEnabled.HasValue)
                entity.IsEnabled = dto.IsEnabled.Value;
            if (dto.Description != null)
                entity.Description = dto.Description;
            if (dto.Tags != null)
                entity.Tags = dto.Tags;

            // AuthToken 语义：null = 保留现有；空字符串 = 清除；非空 = 加密替换
            if (dto.AuthToken != null)
            {
                entity.AuthTokenEncrypted = string.IsNullOrEmpty(dto.AuthToken) ? null : GetProtector().Protect(dto.AuthToken);
            }

            await _repository.UpdateAsync(entity);
            LogInformation("Updated MCP server registration {Id} ({Name})", entity.Id, entity.Name);
            return Ok(ToDto(entity));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating MCP server registration {Id}", id);
            return Fail<McpServerRegistrationDto>("Failed to update MCP server registration", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var entity = await _repository.AsQueryable(withTracking: true).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail("MCP server registration not found", 404, ErrorCodes.McpServerRegistrationNotFound);
            }

            await _repository.DeleteAsync(entity);
            LogInformation("Deleted MCP server registration {Id} ({Name})", id, entity.Name);
            return Ok();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting MCP server registration {Id}", id);
            return Fail("Failed to delete MCP server registration", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<McpServerTestResultDto>> TestConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var entity = await _repository.AsQueryable().FirstOrDefaultAsync(p => p.Id == id, ct);
            if (entity == null)
            {
                return Fail<McpServerTestResultDto>("MCP server registration not found", 404, ErrorCodes.McpServerRegistrationNotFound);
            }

            // Shallow probe — verify entity is enabled, ServerUrl parses as a valid URI,
            // and any stored auth credential decrypts cleanly. We do NOT open a real MCP
            // session (would require spinning up the MCP client stack and may hit network
            // latency/availability issues). Future enhancement: optionally invoke a real
            // MCP client ping when the runtime loading path consumes this table.
            if (!entity.IsEnabled)
            {
                sw.Stop();
                return Ok(new McpServerTestResultDto
                {
                    Success = false,
                    Message = "MCP server registration is disabled",
                    LatencyMs = sw.ElapsedMilliseconds
                });
            }

            if (!Uri.TryCreate(entity.ServerUrl, UriKind.Absolute, out _) &&
                !string.Equals(entity.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                return Ok(new McpServerTestResultDto
                {
                    Success = false,
                    Message = "Server URL is not a valid absolute URI",
                    LatencyMs = sw.ElapsedMilliseconds
                });
            }

            if (!string.IsNullOrEmpty(entity.AuthTokenEncrypted))
            {
                try
                {
                    _ = GetProtector().Unprotect(entity.AuthTokenEncrypted);
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Logger.LogWarning(ex, "Failed to decrypt auth token for MCP server registration {Id}", id);
                    return Ok(new McpServerTestResultDto
                    {
                        Success = false,
                        Message = "Stored auth token could not be decrypted (data protection key rotated?)",
                        LatencyMs = sw.ElapsedMilliseconds
                    });
                }
            }
            else if (!string.IsNullOrEmpty(entity.AuthType) &&
                     !string.Equals(entity.AuthType, "none", StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                return Ok(new McpServerTestResultDto
                {
                    Success = false,
                    Message = $"Auth type '{entity.AuthType}' requires an auth token but none is configured",
                    LatencyMs = sw.ElapsedMilliseconds
                });
            }

            sw.Stop();
            return Ok(new McpServerTestResultDto
            {
                Success = true,
                Message = "MCP server registration is valid (shallow probe — no live MCP connection)",
                LatencyMs = sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "Error testing MCP server registration {Id}", id);
            return Fail<McpServerTestResultDto>("MCP server test failed", 500, ErrorCodes.McpServerRegistrationOperationFailed);
        }
    }
}
