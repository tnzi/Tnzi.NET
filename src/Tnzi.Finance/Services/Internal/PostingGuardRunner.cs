namespace Tnzi.Finance.Services.Internal;

/// <summary>
/// 过账前钩子执行器：顺序执行全部已注册的 <see cref="IFinancePostingGuard"/>，
/// 任一失败即短路返回该失败（未注册任何钩子时零开销放行）
/// </summary>
public sealed class PostingGuardRunner
{
    private readonly IEnumerable<IFinancePostingGuard> _guards;

    public PostingGuardRunner(IEnumerable<IFinancePostingGuard> guards)
    {
        _guards = Check.NotNull(guards);
    }

    /// <summary>执行钩子链（在任何写入发生之前调用）</summary>
    public async Task<Result> CheckAsync(string docType, string docId, FinancePostingOperation operation, object document, CancellationToken cancellationToken)
    {
        FinancePostingGuardContext? context = null;
        foreach (var guard in _guards)
        {
            context ??= new FinancePostingGuardContext
            {
                DocType = docType,
                DocId = docId,
                Operation = operation,
                Document = document
            };

            var result = await guard.CheckAsync(context, cancellationToken);
            if (!result.Succeeded)
                return result;
        }

        return Result.Success();
    }
}
