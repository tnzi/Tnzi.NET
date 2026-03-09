namespace Tnzi.Identity.Services;

/// <summary>
/// 租户管理服务实现
/// </summary>
public class TenantService : ApplicationService, ITenantService
{
    private readonly IRepository<Tenant, Guid> _tenantRepository;
    private readonly TimeProvider _timeProvider;
    private readonly bool _multiTenancyEnabled;

    public TenantService(
        IRepository<Tenant, Guid> tenantRepository,
        IServiceProvider serviceProvider,
        TimeProvider? timeProvider = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(serviceProvider)
    {
        _tenantRepository = Check.NotNull(tenantRepository);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _multiTenancyEnabled = multiTenancyOptions?.Value.Enabled ?? false;
    }

    public async Task<Result<TenantDto>> GetByIdAsync(Guid id)
    {
        if (!_multiTenancyEnabled)
        {
            return FeatureDisabled<TenantDto>();
        }

        var tenant = await _tenantRepository.GetAsync(id);
        if (tenant == null)
        {
            return Fail<TenantDto>("Tenant not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        return Ok(tenant.MapTo<TenantDto>());
    }

    public async Task<Result<TenantDto>> GetByCodeAsync(string code)
    {
        if (!_multiTenancyEnabled)
        {
            return FeatureDisabled<TenantDto>();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Fail<TenantDto>("Tenant code is required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var normalizedCode = code.Trim().ToLowerInvariant();
        var tenant = await _tenantRepository
            .Where(t => t.Code != null && t.Code.ToLower() == normalizedCode && !t.IsDeleted)
            .FirstOrDefaultAsync();
        if (tenant == null)
        {
            return Fail<TenantDto>("Tenant not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        return Ok(tenant.MapTo<TenantDto>());
    }

    public async Task<Result<IPagedList<TenantDto>>> GetListAsync(TenantQueryDto query)
    {
        if (!_multiTenancyEnabled)
        {
            return FeatureDisabled<IPagedList<TenantDto>>();
        }

        var tenantQuery = _tenantRepository.AsQueryable().Where(t => !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            tenantQuery = tenantQuery.Where(t =>
                t.Name.ToLower().Contains(keyword) ||
                (t.Code != null && t.Code.ToLower().Contains(keyword)));
        }

        if (query.IsEnabled.HasValue)
        {
            tenantQuery = tenantQuery.Where(t => t.IsEnabled == query.IsEnabled.Value);
        }

        var paged = await tenantQuery
            .OrderByDescending(t => t.CreationTime)
            .ProjectTo<Tenant, TenantDto>()
            .CreateAsync(query);

        return Ok(paged);
    }

    public async Task<Result<TenantDto>> CreateAsync(CreateTenantDto input)
    {
        if (!_multiTenancyEnabled)
        {
            return FeatureDisabled<TenantDto>();
        }

        if (!string.IsNullOrWhiteSpace(input.Code))
        {
            var normalizedCode = input.Code.Trim().ToLowerInvariant();
            var exists = await _tenantRepository
                .AnyAsync(t => t.Code != null && t.Code.ToLower() == normalizedCode && !t.IsDeleted);
            if (exists)
            {
                return Fail<TenantDto>($"Tenant code '{input.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
            }
        }

        var tenant = input.MapTo<Tenant>();
        tenant.Code = string.IsNullOrWhiteSpace(input.Code) ? null : input.Code.Trim();

        try
        {
            await _tenantRepository.InsertAsync(tenant);
        }
        catch (DbUpdateException ex)
        {
            if (ex.IsUniqueConstraintViolation())
            {
                return Fail<TenantDto>($"Tenant code '{input.Code}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
            }

            throw;
        }

        return Ok(tenant.MapTo<TenantDto>());
    }

    public async Task<Result<TenantDto>> UpdateAsync(Guid id, UpdateTenantDto input)
    {
        if (!_multiTenancyEnabled)
        {
            return FeatureDisabled<TenantDto>();
        }

        var tenant = await _tenantRepository.GetAsync(id);
        if (tenant == null)
        {
            return Fail<TenantDto>("Tenant not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var newCode = string.IsNullOrWhiteSpace(input.Code) ? null : input.Code.Trim();
        if (!string.Equals(tenant.Code, newCode, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(newCode))
        {
            var normalizedCode = newCode.ToLowerInvariant();
            var exists = await _tenantRepository
                .AnyAsync(t => t.Id != id && t.Code != null && t.Code.ToLower() == normalizedCode && !t.IsDeleted);
            if (exists)
            {
                return Fail<TenantDto>($"Tenant code '{newCode}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
            }
        }

        tenant.Name = input.Name;
        tenant.Code = newCode;
        tenant.IsEnabled = input.IsEnabled;
        tenant.ExpiredAt = input.ExpiredAt;
        tenant.Remark = input.Remark;

        try
        {
            await _tenantRepository.UpdateAsync(tenant);
        }
        catch (DbUpdateException ex)
        {
            if (ex.IsUniqueConstraintViolation())
            {
                return Fail<TenantDto>($"Tenant code '{newCode}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
            }

            throw;
        }

        return Ok(tenant.MapTo<TenantDto>());
    }

    public async Task<Result> SetEnabledAsync(Guid id, bool enabled)
    {
        if (!_multiTenancyEnabled)
        {
            return FeatureDisabled();
        }

        var tenant = await _tenantRepository.GetAsync(id);
        if (tenant == null)
        {
            return Fail("Tenant not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        tenant.IsEnabled = enabled;
        await _tenantRepository.UpdateAsync(tenant);
        return Ok();
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        if (!_multiTenancyEnabled)
        {
            return FeatureDisabled();
        }

        var tenant = await _tenantRepository.GetAsync(id);
        if (tenant == null)
        {
            return Fail("Tenant not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        await _tenantRepository.DeleteAsync(id);
        return Ok();
    }

    public async Task<bool> IsActiveAsync(Guid tenantId)
    {
        if (!_multiTenancyEnabled)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return await _tenantRepository.AnyAsync(t =>
            t.Id == tenantId &&
            !t.IsDeleted &&
            t.IsEnabled &&
            (!t.ExpiredAt.HasValue || t.ExpiredAt > now));
    }

    private Result FeatureDisabled()
    {
        return Fail("Multi-tenancy is disabled", 400, ErrorCodes.VALIDATION_ERROR);
    }

    private Result<T> FeatureDisabled<T>()
    {
        return Fail<T>("Multi-tenancy is disabled", 400, ErrorCodes.VALIDATION_ERROR);
    }
}
