using Microsoft.Extensions.Logging.Abstractions;

namespace Tnzi.Payment.Subscriptions.Tests;

/// <summary>
/// 单元测试手工构造服务时用的计费账户替身：默认的 User 范围解析器（与引入计费账户之前逐字相同），
/// 以及一个没有任何账户资料的资料服务（联系人一律取调用方给的兜底值）。
/// </summary>
internal static class TestBillingAccounts
{
    public static IBillingAccountResolver UserScope()
        => new DefaultBillingAccountResolver(
            new StaticOptionsMonitor<BillingAccountOptions>(new BillingAccountOptions()),
            NullLogger<DefaultBillingAccountResolver>.Instance);

    public static IBillingAccountService NoProfiles() => new NoProfileBillingAccountService();

    private sealed class NoProfileBillingAccountService : IBillingAccountService
    {
        public Task<Result<BillingAccountDto>> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure<BillingAccountDto>("Not available in this test.", 501));

        public Task<Result<BillingAccountDto>> UpdateCurrentAsync(Guid userId, UpdateBillingAccountDto request, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure<BillingAccountDto>("Not available in this test.", 501));

        public Task<BillingContact> ResolveContactAsync(BillingAccountRef? account, string? fallbackName, string? fallbackEmail, CancellationToken cancellationToken = default)
            => Task.FromResult(new BillingContact(fallbackName, string.IsNullOrWhiteSpace(fallbackEmail) ? [] : [fallbackEmail]));
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
