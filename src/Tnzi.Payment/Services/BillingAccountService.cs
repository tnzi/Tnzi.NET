namespace Tnzi.Payment.Services;

/// <summary>
/// 计费账户资料服务实现
/// </summary>
public class BillingAccountService : ApplicationService, IBillingAccountService
{
    private readonly IRepository<BillingAccount, Guid> _accountRepository;
    private readonly IBillingAccountResolver _accountResolver;
    private readonly IUserEmailProvider? _userEmails;
    private readonly IUserDisplayNameProvider? _userDisplayNames;
    private readonly IBillingAccountProfileProvider? _profileProvider;

    public BillingAccountService(
        IRepository<BillingAccount, Guid> accountRepository,
        IBillingAccountResolver accountResolver,
        IServiceProvider serviceProvider,
        IUserEmailProvider? userEmails = null,
        IUserDisplayNameProvider? userDisplayNames = null,
        IBillingAccountProfileProvider? profileProvider = null)
        : base(serviceProvider)
    {
        _accountRepository = Check.NotNull(accountRepository);
        _accountResolver = Check.NotNull(accountResolver);
        _userEmails = userEmails;
        _userDisplayNames = userDisplayNames;
        _profileProvider = profileProvider;
    }

    public async Task<Result<BillingAccountDto>> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var access = await _accountResolver.ResolveAsync(userId, cancellationToken);
        if (access == null || !access.CanView)
            return Fail<BillingAccountDto>(ErrorCodes.BillingAccountAccessDenied, 403);

        var profile = await FindProfileAsync(access.Account, cancellationToken);
        return Ok(ToDto(access, profile));
    }

    public async Task<Result<BillingAccountDto>> UpdateCurrentAsync(Guid userId, UpdateBillingAccountDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var access = await _accountResolver.ResolveAsync(userId, cancellationToken);
        if (access == null || !access.CanManage)
            return Fail<BillingAccountDto>(ErrorCodes.BillingAccountAccessDenied, 403);

        var emails = (request.BillingEmails ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invalid = emails.FirstOrDefault(e => !new EmailAddressAttribute().IsValid(e) || e.Contains(';') || e.Contains(','));
        if (invalid != null)
            return Fail<BillingAccountDto>($"'{invalid}' is not a valid email address.", 400);

        var joined = emails.Count == 0 ? null : string.Join(";", emails);
        if (joined is { Length: > BillingAccount.BillingEmailsMaxLength })
            return Fail<BillingAccountDto>($"Billing emails cannot exceed {BillingAccount.BillingEmailsMaxLength} characters in total.", 400);

        var profile = await _accountRepository.FirstOrDefaultAsync(
            a => a.AccountType == access.Account.Type && a.AccountId == access.Account.Id, cancellationToken);

        var isNew = profile == null;
        profile ??= new BillingAccount { AccountType = access.Account.Type, AccountId = access.Account.Id };
        profile.DisplayName = Normalize(request.DisplayName);
        profile.BillingEmails = joined;
        profile.Address = Normalize(request.Address);
        profile.TaxId = Normalize(request.TaxId);

        if (isNew)
            await _accountRepository.InsertAsync(profile, cancellationToken);
        else
            await _accountRepository.UpdateAsync(profile, cancellationToken);

        Logger.LogInformation("Billing account {Account} profile updated by {UserId}.", access.Account, userId);
        return Ok(ToDto(access, profile));
    }

    public Task<BillingContact> ResolveContactAsync(BillingAccountRef? account, string? fallbackName, string? fallbackEmail, CancellationToken cancellationToken = default)
        => ResolveContactAsync(account, fallbackName, fallbackEmail, null, cancellationToken);

    public async Task<BillingContact> ResolveContactAsync(BillingAccountRef? account, string? fallbackName, string? fallbackEmail, Guid? fallbackUserId, CancellationToken cancellationToken = default)
    {
        // 逐项取：账户自己的资料 → 消费应用的资料 → 经办用户（显示名与账号邮箱）
        var profile = account == null ? null : await FindProfileAsync(account, cancellationToken);
        var name = Normalize(profile?.DisplayName);
        var address = Normalize(profile?.Address);
        var taxId = Normalize(profile?.TaxId);
        string? company = null;
        IReadOnlyList<string> emails = profile?.GetBillingEmails() ?? [];

        if (account != null && _profileProvider != null)
        {
            var supplied = await _profileProvider.GetProfileAsync(account, cancellationToken);
            if (supplied != null)
            {
                name ??= Normalize(supplied.DisplayName);
                company = Normalize(supplied.Company);
                address ??= Normalize(supplied.Address);
                taxId ??= Normalize(supplied.TaxId);
                if (emails.Count == 0)
                    emails = CleanEmails(supplied.Emails);
            }
        }

        if (emails.Count == 0 && !string.IsNullOrWhiteSpace(fallbackEmail))
            emails = [fallbackEmail];

        var userId = fallbackUserId is { } id && id != Guid.Empty ? id : (Guid?)null;

        // 发起人账号上的邮箱（令牌不带邮箱声明、或后台任务没有当前用户时）
        if (emails.Count == 0 && userId != null && _userEmails != null)
        {
            var found = await _userEmails.GetEmailsAsync([userId.Value], cancellationToken);
            if (found.TryGetValue(userId.Value, out var userEmail))
                emails = [userEmail];
        }

        // 落到用户身上时用这个人的显示名：调用方给的后备名通常是登录名，印在票据上的应是人名
        if (name == null && userId != null && _userDisplayNames != null)
        {
            var found = await _userDisplayNames.GetDisplayNamesAsync([userId.Value], cancellationToken);
            if (found.TryGetValue(userId.Value, out var displayName))
                name = Normalize(displayName);
        }

        return new BillingContact(name ?? fallbackName, emails, company, address, taxId);
    }

    private static List<string> CleanEmails(IReadOnlyList<string>? emails)
        => (emails ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private Task<BillingAccount?> FindProfileAsync(BillingAccountRef account, CancellationToken cancellationToken)
        => _accountRepository.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AccountType == account.Type && a.AccountId == account.Id, cancellationToken);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static BillingAccountDto ToDto(BillingAccountAccess access, BillingAccount? profile) => new()
    {
        AccountType = access.Account.Type,
        AccountId = access.Account.Id,
        CanManage = access.CanManage,
        DisplayName = profile?.DisplayName,
        BillingEmails = profile?.GetBillingEmails().ToList() ?? [],
        Address = profile?.Address,
        TaxId = profile?.TaxId
    };
}
