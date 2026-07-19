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

        // 被单据引用时拒绝删除：Vendor 软删后会被全局过滤器隐藏，而其已过账账单/费用/付款仍留在子账，
        // 导致往来方名字丢失、账龄回退显示原始 GUID。引导用 IsActive=false 停用而非删除（对齐 Tax/BankAccount 守卫）。
        // 引用仓储在删除冷路径按需解析（Payroll 影子供应商链路复用本服务但从不调用删除，其最小测试基类未注册
        // Bill/Expense 仓储且未建模，故不能强加为构造依赖）。
        var billRepository = GetRequiredService<IRepository<Bill, Guid>>();
        var expenseRepository = GetRequiredService<IRepository<Expense, Guid>>();
        var paymentRepository = GetRequiredService<IRepository<PaymentEntry, Guid>>();
        var referenced =
            await billRepository.AnyAsync(b => b.VendorId == id, cancellationToken) ||
            await expenseRepository.AnyAsync(e => e.VendorId == id, cancellationToken) ||
            await paymentRepository.AnyAsync(p => p.PartyType == FinancePartyType.Vendor && p.PartyId == id, cancellationToken);
        if (referenced)
            return Fail("Cannot delete a vendor referenced by bills, expenses, or payments. Deactivate it instead.", 409);

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
