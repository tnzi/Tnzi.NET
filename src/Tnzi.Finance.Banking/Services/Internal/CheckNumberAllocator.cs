namespace Tnzi.Finance.Banking.Services.Internal;

/// <summary>
/// 支票号分配器（per-bank-account 原子递增）
/// </summary>
/// <remarks>
/// 照抄 <see cref="DocumentNumberService"/> 的事务语义，但作用在 <see cref="BankAccount.NextCheckNumber"/>：
/// 先 <see cref="IUnitOfWork.EnsureTransactionStartedAsync"/> 强制开启物理事务，再 <c>ExecuteUpdateAsync</c>
/// 原子递增（行锁将并发分配串行化）+ 同事务读回。号码经调用方 UoW 回滚回收（打印事务失败=号码归还）。
/// 与无缺口连续号不同：支票号允许缺口（跳号=换票本，毁票占号留痕），因此不复用 <see cref="IDocumentNumberService"/>。
/// 分配全程只走 SQL（不触碰被跟踪的 BankAccount 实体），避免与实体级更新（毁票推进/手工设号）互相覆盖。
/// </remarks>
public sealed class CheckNumberAllocator
{
    private readonly IRepository<BankAccount, Guid> _bankAccountRepository;

    public CheckNumberAllocator(IRepository<BankAccount, Guid> bankAccountRepository)
    {
        _bankAccountRepository = Check.NotNull(bankAccountRepository);
    }

    /// <summary>
    /// 为指定银行账户分配下一张支票号（原子递增，须在活动事务内调用以保证回滚回收）。
    /// </summary>
    /// <exception cref="ConflictException">银行账户不存在（并发删除）</exception>
    public async Task<long> AllocateAsync(Guid bankAccountId, CancellationToken cancellationToken = default)
    {
        await _bankAccountRepository.EnsureTransactionStartedAsync(cancellationToken);

        var updated = await _bankAccountRepository.AsQueryable(true)
            .Where(b => b.Id == bankAccountId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckNumber, x => x.NextCheckNumber + 1), cancellationToken);

        if (updated == 0)
            throw new ConflictException($"Bank account '{bankAccountId}' no longer exists; cannot allocate a check number.");

        var next = await _bankAccountRepository.AsQueryable()
            .Where(b => b.Id == bankAccountId)
            .Select(b => b.NextCheckNumber)
            .FirstAsync(cancellationToken);

        return next - 1;
    }
}
