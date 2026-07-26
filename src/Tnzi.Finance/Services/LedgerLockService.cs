namespace Tnzi.Finance.Services;

/// <summary>
/// 账本封账锁服务
/// </summary>
public class LedgerLockService : ApplicationService, ILedgerLockService
{
    private readonly IRepository<LedgerLock, Guid> _repository;

    public LedgerLockService(IServiceProvider serviceProvider, IRepository<LedgerLock, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Result<LedgerLockDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(tracked: false, cancellationToken);
        return Ok(ToDto(entity));
    }

    public async Task<Result<LedgerLockDto>> SetAsync(SetLedgerLockDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var entity = await LoadAsync(tracked: true, cancellationToken);

        // 已设口令 → 必须先证明你知道它。校验在任何写入之前，拒绝路径零副作用。
        if (entity?.PasswordHash != null && !HashHelper.VerifyPassword(input.Password ?? string.Empty, entity.PasswordHash))
            return Fail<LedgerLockDto>("The closing-date password is incorrect.", 403);

        var closingDate = input.ClosingDate?.ToUtcDate();

        // 封到未来没有意义（未来还没发生的交易谈不上"已报出去了"），而且会把正常的
        // 当期记账整片挡死。这是最容易手滑打错年份的输入，值得挡在前面。
        // 多给一天是给时区留的余量：UTC+13 的记账员眼里的"今天"在服务端已经是明天，
        // 按 UTC 当日严格判定会把一个完全正常的操作拒掉。消息如实说明这一天。
        if (closingDate.HasValue && closingDate.Value > DateTime.UtcNow.Date.AddDays(1))
            return Fail<LedgerLockDto>("The closing date cannot be more than one day in the future.", 400);

        entity ??= new LedgerLock { Scope = LedgerLock.SingletonScope };

        entity.ClosingDate = closingDate;
        entity.Note = input.Note;

        // NewPassword 的三态语义与其它可选修改字段一致：null=不动 / 空串=清除 / 有值=设置
        if (input.NewPassword != null)
        {
            entity.PasswordHash = input.NewPassword.Length == 0
                ? null
                : HashHelper.HashPassword(input.NewPassword);
        }

        try
        {
            if (entity.Id == default)
                await _repository.InsertAsync(entity, cancellationToken);
            else
                await _repository.UpdateAsync(entity, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            // 并发首次设定：单行唯一索引兜底 get-or-create 的 check-then-act 竞态。
            return Fail<LedgerLockDto>("The ledger lock was changed concurrently. Reload and try again.", 409);
        }

        return Ok(ToDto(entity));
    }

    public async Task<Result> ValidatePostingDateAsync(DateTime postingDate, CancellationToken cancellationToken = default)
    {
        var closingDate = await _repository.AsNoTracking()
            .Where(l => l.Scope == LedgerLock.SingletonScope)
            .Select(l => l.ClosingDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (closingDate == null)
            return Ok();

        var date = postingDate.ToUtcDate();
        if (date > closingDate.Value)
            return Ok();

        return Fail(
            $"Posting date {date:yyyy-MM-dd} is on or before the closing date {closingDate.Value:yyyy-MM-dd}. " +
            "The books are closed through that date; move the closing date back to post into a closed period.",
            409);
    }

    /// <summary>取本租户唯一那行（租户维度交给全局查询过滤器，谓词不引用 TenantId）。</summary>
    private Task<LedgerLock?> LoadAsync(bool tracked, CancellationToken cancellationToken)
        => (tracked ? _repository.AsQueryable(true) : _repository.AsNoTracking())
            .FirstOrDefaultAsync(l => l.Scope == LedgerLock.SingletonScope, cancellationToken);

    private static LedgerLockDto ToDto(LedgerLock? entity) => new()
    {
        ClosingDate = entity?.ClosingDate,
        IsPasswordProtected = entity?.PasswordHash != null,
        Note = entity?.Note,
        LastChangedTime = entity?.LastModificationTime ?? entity?.CreationTime,
        LastChangedBy = entity?.LastModifierId ?? entity?.CreatorId,
    };
}
