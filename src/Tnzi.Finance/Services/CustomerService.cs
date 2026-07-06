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

        // P2b：被未清单据引用时拒绝删除
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
