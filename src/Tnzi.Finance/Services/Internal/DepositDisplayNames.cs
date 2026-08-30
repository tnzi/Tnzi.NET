namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 把存款单 DTO 上的 id 换成人看得懂的名字（科目、收款单、往来方）。
/// </summary>
/// <remarks>
/// 纯读侧投影：不参与草稿组装、过账或作废，只在把单据交给调用方之前补上显示名。
/// 与 <c>DepositService</c> 分开，是因为那边管的是「这张存款单发生了什么」，
/// 这里只管「怎么把它显示出来」——两者一起变的时候极少。
/// </remarks>
internal sealed class DepositDisplayNames
{
    private readonly IReadOnlyRepository<PaymentEntry, Guid> _paymentRepository;
    private readonly IReadOnlyRepository<Account, Guid> _accountRepository;
    private readonly IReadOnlyRepository<Customer, Guid> _customerRepository;

    public DepositDisplayNames(
        IReadOnlyRepository<PaymentEntry, Guid> paymentRepository,
        IReadOnlyRepository<Account, Guid> accountRepository,
        IReadOnlyRepository<Customer, Guid> customerRepository)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _accountRepository = Check.NotNull(accountRepository);
        _customerRepository = Check.NotNull(customerRepository);
    }

    public async Task FillAccountNamesAsync(IList<DepositDto> items, CancellationToken cancellationToken)
    {
        var accountIds = items.SelectMany(d => new[] { d.FromAccountId, d.ToAccountId }).Distinct().ToList();
        if (accountIds.Count == 0)
            return;

        var names = await LoadAccountNamesAsync(accountIds, cancellationToken);
        foreach (var dto in items)
        {
            dto.FromAccountName = names.GetValueOrDefault(dto.FromAccountId);
            dto.ToAccountName = names.GetValueOrDefault(dto.ToAccountId);
        }
    }

    public async Task FillLineLabelsAsync(IList<DepositLineDto> lines, CancellationToken cancellationToken)
    {
        var accountIds = lines.Where(l => l.AccountId.HasValue).Select(l => l.AccountId!.Value).Distinct().ToList();
        if (accountIds.Count > 0)
        {
            var names = await LoadAccountNamesAsync(accountIds, cancellationToken);
            foreach (var line in lines.Where(l => l.AccountId.HasValue))
                line.AccountName = names.GetValueOrDefault(line.AccountId!.Value);
        }

        var paymentIds = lines.Where(l => l.PaymentEntryId.HasValue).Select(l => l.PaymentEntryId!.Value).Distinct().ToList();
        if (paymentIds.Count == 0)
            return;

        var payments = await _paymentRepository.AsNoTracking()
            .Where(p => paymentIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Number, p.PartyType, p.PartyId })
            .ToListAsync(cancellationToken);
        var byId = payments.ToDictionary(p => p.Id);

        var customerNames = await LoadCustomerNamesAsync(
            payments.Where(p => p.PartyType == FinancePartyType.Customer).Select(p => p.PartyId), cancellationToken);

        foreach (var line in lines.Where(l => l.PaymentEntryId.HasValue))
        {
            if (!byId.TryGetValue(line.PaymentEntryId!.Value, out var payment))
                continue;
            line.PaymentNumber = payment.Number;
            line.PartyName = customerNames.GetValueOrDefault(payment.PartyId);
        }
    }

    public async Task<Dictionary<Guid, string>> LoadAccountNamesAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
        => await _accountRepository.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Code, a.Name })
            .ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", cancellationToken);

    public async Task<Dictionary<Guid, string>> LoadCustomerNamesAsync(IEnumerable<Guid> partyIds, CancellationToken cancellationToken)
    {
        var ids = partyIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        return await _customerRepository.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
    }
}
