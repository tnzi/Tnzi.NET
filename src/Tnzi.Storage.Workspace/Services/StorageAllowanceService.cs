namespace Tnzi.Storage.Workspace.Services;

/// <summary>
/// <see cref="IStorageAllowanceService"/> 的默认实现。授权在管理端控制器（类级 <c>storage.file.view</c>、写端点 <c>storage.file.update</c>）。
/// </summary>
public class StorageAllowanceService : ApplicationService, IStorageAllowanceService
{
    private readonly IRepository<StorageAllowance, Guid> _allowanceRepository;

    public StorageAllowanceService(IServiceProvider serviceProvider, IRepository<StorageAllowance, Guid> allowanceRepository)
        : base(serviceProvider)
    {
        _allowanceRepository = Check.NotNull(allowanceRepository);
    }

    public async Task<Result<IPagedList<StorageAllowanceDto>>> GetListAsync(StorageAllowanceQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var allowances = _allowanceRepository.AsQueryable();
        var total = await allowances.CountAsync(cancellationToken);
        var page = await allowances
            .OrderByDescending(a => a.LastModificationTime ?? a.CreationTime)
            .ThenBy(a => a.Id)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        return Ok<IPagedList<StorageAllowanceDto>>(
            new PagedList<StorageAllowanceDto>(page.Select(MapToDto).ToList(), query.PageIndex, query.PageSize, total));
    }

    public async Task<Result<StorageAllowanceDto>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var allowance = await _allowanceRepository.FirstOrDefaultAsync(a => a.UserId == userId, cancellationToken);
        return allowance == null
            ? Fail<StorageAllowanceDto>("This user has no storage allowance of their own; the default applies", 404, ErrorCodes.RESOURCE_NOT_FOUND)
            : Ok(MapToDto(allowance));
    }

    public async Task<Result<StorageAllowanceDto>> SetAsync(Guid userId, SetStorageAllowanceRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (userId == Guid.Empty)
            return Fail<StorageAllowanceDto>("A user id is required", 400, ErrorCodes.VALIDATION_ERROR);

        if (request.AllowanceBytes < 0)
            return Fail<StorageAllowanceDto>("The allowance cannot be negative (leave it empty for no limit)", 400, ErrorCodes.VALIDATION_ERROR);

        // 读出来改再写回是安全的：这一行只有管理员会写，没有原子计数之类要防覆盖的列。
        var existing = await _allowanceRepository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(a => a.UserId == userId, cancellationToken);

        if (existing == null)
        {
            existing = new StorageAllowance
            {
                UserId = userId,
                AllowanceBytes = request.AllowanceBytes,
                Note = request.Note
            };
            await _allowanceRepository.InsertAsync(existing, cancellationToken);
        }
        else
        {
            existing.AllowanceBytes = request.AllowanceBytes;
            existing.Note = request.Note;
            await _allowanceRepository.UpdateAsync(existing, cancellationToken);
        }

        LogInformation("Storage allowance of user {UserId} set to {AllowanceBytes}", userId, request.AllowanceBytes?.ToString() ?? "unlimited");
        return Ok(MapToDto(existing));
    }

    public async Task<Result> RemoveAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _allowanceRepository.DeleteAsync(a => a.UserId == userId, cancellationToken);
        LogInformation("Storage allowance of user {UserId} removed; the default applies", userId);
        return Ok();
    }

    private static StorageAllowanceDto MapToDto(StorageAllowance allowance) => new()
    {
        UserId = allowance.UserId,
        AllowanceBytes = allowance.AllowanceBytes,
        Note = allowance.Note,
        CreationTime = allowance.CreationTime,
        LastModificationTime = allowance.LastModificationTime
    };
}
