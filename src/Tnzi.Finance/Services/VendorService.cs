namespace Tnzi.Finance.Services;

/// <summary>
/// 供应商服务
/// </summary>
public class VendorService : ApplicationService, IVendorService
{
    private readonly IRepository<Vendor, Guid> _vendorRepository;

    public VendorService(IServiceProvider serviceProvider, IRepository<Vendor, Guid> vendorRepository)
        : base(serviceProvider)
    {
        _vendorRepository = Check.NotNull(vendorRepository);
    }

    public async Task<Result<IPagedList<VendorDto>>> GetPagedAsync(VendorQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _vendorRepository.AsNoTracking()
            .Filter(query)
            .OrderBy(v => v.Name)
            .ProjectTo<Vendor, VendorDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<VendorDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var vendor = await _vendorRepository.GetAsync(id, cancellationToken);
        if (vendor == null)
            return Fail<VendorDto>("Vendor not found.", 404);

        return Ok(vendor.MapTo<VendorDto>());
    }

    public async Task<Result<VendorDto>> CreateAsync(CreateVendorDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateAsync(input, excludeId: null, cancellationToken);
        if (!validation.Succeeded)
            return Fail<VendorDto>(validation.Message ?? "Invalid vendor.", validation.Code ?? 400);

        var vendor = new Vendor();
        Apply(vendor, input, isActive: true);

        try
        {
            await _vendorRepository.InsertAsync(vendor, cancellationToken);
            await _vendorRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<VendorDto>($"Vendor code '{vendor.Code}' already exists.", 409);
        }

        return Ok(vendor.MapTo<VendorDto>());
    }

    public async Task<Result<VendorDto>> UpdateAsync(Guid id, UpdateVendorDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var vendor = await _vendorRepository.GetAsync(id, cancellationToken);
        if (vendor == null)
            return Fail<VendorDto>("Vendor not found.", 404);

        var validation = await ValidateAsync(input, excludeId: id, cancellationToken);
        if (!validation.Succeeded)
            return Fail<VendorDto>(validation.Message ?? "Invalid vendor.", validation.Code ?? 400);

        Apply(vendor, input, input.IsActive);

        try
        {
            await _vendorRepository.UpdateAsync(vendor, cancellationToken);
            await _vendorRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<VendorDto>($"Vendor code '{vendor.Code}' already exists.", 409);
        }

        return Ok(vendor.MapTo<VendorDto>());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var vendor = await _vendorRepository.GetAsync(id, cancellationToken);
        if (vendor == null)
            return Fail("Vendor not found.", 404);

        // P2b：被未清单据引用时拒绝删除
        await _vendorRepository.DeleteAsync(vendor, cancellationToken);
        return Ok();
    }

    public async Task<Vendor?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);
        var normalized = code.Trim();
        return await _vendorRepository.FindAsync(v => v.Code == normalized, cancellationToken);
    }

    private async Task<Result> ValidateAsync(CreateVendorDto input, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Vendor name is required.");

        var code = input.Code?.Trim();
        if (!string.IsNullOrEmpty(code) &&
            await _vendorRepository.AnyAsync(v => v.Code == code && v.Id != excludeId, cancellationToken))
        {
            return Fail($"Vendor code '{code}' already exists.", 409);
        }

        return Ok();
    }

    private static void Apply(Vendor vendor, CreateVendorDto input, bool isActive)
    {
        vendor.Code = string.IsNullOrWhiteSpace(input.Code) ? null : input.Code.Trim();
        vendor.Name = input.Name.Trim();
        vendor.Email = input.Email?.Trim();
        vendor.Phone = input.Phone?.Trim();
        vendor.Address = input.Address;
        vendor.Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant();
        vendor.PaymentTermsDays = input.PaymentTermsDays;
        vendor.DefaultTaxCodeId = input.DefaultTaxCodeId;
        vendor.Notes = input.Notes;
        vendor.IsActive = isActive;
    }
}
