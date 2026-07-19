namespace Tnzi.Finance.Services;

/// <summary>
/// 客户服务
/// </summary>
public class CustomerService : ApplicationService, ICustomerService
{
    private readonly IRepository<Customer, Guid> _customerRepository;

    public CustomerService(IServiceProvider serviceProvider, IRepository<Customer, Guid> customerRepository)
        : base(serviceProvider)
    {
        _customerRepository = Check.NotNull(customerRepository);
    }

    public async Task<Result<IPagedList<CustomerDto>>> GetPagedAsync(CustomerQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _customerRepository.AsNoTracking()
            .Filter(query)
            .OrderBy(c => c.Name)
            .ProjectTo<Customer, CustomerDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<CustomerDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _customerRepository.GetAsync(id, cancellationToken);
        if (customer == null)
            return Fail<CustomerDto>("Customer not found.", 404);

        return Ok(customer.MapTo<CustomerDto>());
    }

    public async Task<Result<CustomerDto>> CreateAsync(CreateCustomerDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var validation = await ValidateAsync(input, excludeId: null, cancellationToken);
        if (!validation.Succeeded)
            return Fail<CustomerDto>(validation.Message ?? "Invalid customer.", validation.Code ?? 400);

        var customer = new Customer();
        Apply(customer, input, isActive: true);

        try
        {
            await _customerRepository.InsertAsync(customer, cancellationToken);
            await _customerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<CustomerDto>($"Customer code '{customer.Code}' already exists.", 409);
        }

        return Ok(customer.MapTo<CustomerDto>());
    }

    public async Task<Result<CustomerDto>> UpdateAsync(Guid id, UpdateCustomerDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var customer = await _customerRepository.GetAsync(id, cancellationToken);
        if (customer == null)
            return Fail<CustomerDto>("Customer not found.", 404);

        var validation = await ValidateAsync(input, excludeId: id, cancellationToken);
        if (!validation.Succeeded)
            return Fail<CustomerDto>(validation.Message ?? "Invalid customer.", validation.Code ?? 400);

        Apply(customer, input, input.IsActive);

        try
        {
            await _customerRepository.UpdateAsync(customer, cancellationToken);
            await _customerRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            return Fail<CustomerDto>($"Customer code '{customer.Code}' already exists.", 409);
        }

        return Ok(customer.MapTo<CustomerDto>());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await _customerRepository.GetAsync(id, cancellationToken);
        if (customer == null)
            return Fail("Customer not found.", 404);

        // 被单据引用时拒绝删除：Customer 软删后会被全局过滤器隐藏，而其已过账发票/贷项/收款仍留在子账，
        // 导致往来方名字丢失、账龄回退显示原始 GUID。引导用 IsActive=false 停用而非删除（对齐 Tax/BankAccount 守卫）。
        // 引用仓储在删除冷路径按需解析（避免把 Invoice/CreditMemo/Payment 依赖强加到共享 VendorService 图上的
        // Payroll 最小测试基类；那里从不调用本删除路径）。
        var invoiceRepository = GetRequiredService<IRepository<Invoice, Guid>>();
        var creditMemoRepository = GetRequiredService<IRepository<CreditMemo, Guid>>();
        var paymentRepository = GetRequiredService<IRepository<PaymentEntry, Guid>>();
        var referenced =
            await invoiceRepository.AnyAsync(i => i.CustomerId == id, cancellationToken) ||
            await creditMemoRepository.AnyAsync(c => c.CustomerId == id, cancellationToken) ||
            await paymentRepository.AnyAsync(p => p.PartyType == FinancePartyType.Customer && p.PartyId == id, cancellationToken);
        if (referenced)
            return Fail("Cannot delete a customer referenced by invoices, credit memos, or payments. Deactivate it instead.", 409);

        await _customerRepository.DeleteAsync(customer, cancellationToken);
        return Ok();
    }

    public async Task<Customer?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(code);
        var normalized = code.Trim();
        return await _customerRepository.FindAsync(c => c.Code == normalized, cancellationToken);
    }

    private async Task<Result> ValidateAsync(CreateCustomerDto input, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return Fail("Customer name is required.");

        var code = input.Code?.Trim();
        if (!string.IsNullOrEmpty(code) &&
            await _customerRepository.AnyAsync(c => c.Code == code && c.Id != excludeId, cancellationToken))
        {
            return Fail($"Customer code '{code}' already exists.", 409);
        }

        return Ok();
    }

    private static void Apply(Customer customer, CreateCustomerDto input, bool isActive)
    {
        customer.Code = string.IsNullOrWhiteSpace(input.Code) ? null : input.Code.Trim();
        customer.Name = input.Name.Trim();
        customer.Email = input.Email?.Trim();
        customer.Phone = input.Phone?.Trim();
        customer.BillingAddress = input.BillingAddress;
        customer.ShippingAddress = input.ShippingAddress;
        customer.Currency = string.IsNullOrWhiteSpace(input.Currency) ? null : input.Currency.Trim().ToUpperInvariant();
        customer.PaymentTermsDays = input.PaymentTermsDays;
        customer.DefaultTaxCodeId = input.DefaultTaxCodeId;
        customer.Notes = input.Notes;
        customer.IsActive = isActive;
    }
}
