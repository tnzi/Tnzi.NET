using Microsoft.Extensions.DependencyInjection;
using Tnzi.Data;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using Tnzi.TestBase;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 绑卡链路集成测试。
/// </summary>
/// <remarks>
/// 这条链路此前整体缺失：绑卡结果全仓只读不写，导致后台续费、试用转正、升级补差在开箱状态下
/// 必然走"无支付方式"分支降级 PastDue。
///
/// ★ 本文件跑的是<b>没装续费包</b>的宿主：没有任何 <c>IStoredPaymentMethodBindingSink</c> 注册，
/// 绑卡与解绑照常成功，只是没有下游需要同步。「绑卡 → 订阅同步」那四条用例搬去了
/// <c>Tnzi.Payment.Subscriptions.Tests/Integration/PaymentMethodBindingSinkIntegrationTests</c>，
/// 在那边它们证明的是「经过一层扩展点之后这条链路仍然接得上」。
/// </remarks>
public class PaymentMethodIntegrationTests : PaymentIntegrationTestBase
{
    private static readonly Guid UserId = TestHelper.DefaultTestUserId;

    private Task<Result<StoredPaymentMethodDto>> BindAsync(string token, bool setAsDefault = true) =>
        InScopeAsync<IPaymentMethodService, Result<StoredPaymentMethodDto>>(
            svc => svc.BindAsync(UserId, new BindPaymentMethodDto
            {
                PaymentMethodToken = token,
                ChannelCode = "Null",
                SetAsDefault = setAsDefault
            }));

    [Fact]
    public async Task CreateSetupSession_ReturnsClientSecret()
    {
        var session = await InScopeAsync<IPaymentMethodService, Result<SetupSessionDto>>(
            svc => svc.CreateSetupSessionAsync(UserId, new CreateSetupSessionDto { ChannelCode = "Null" }));

        session.Succeeded.ShouldBeTrue();
        session.Data!.ClientSecret.ShouldNotBeNullOrWhiteSpace();
        session.Data.ChannelCode.ShouldBe("Null");
    }

    [Fact]
    public async Task Bind_PersistsMethodAndMarksFirstAsDefault()
    {
        var bound = await BindAsync("pm_test_1", setAsDefault: false);

        bound.Succeeded.ShouldBeTrue();
        // 首个支付方式必须自动成为默认，否则后台扣款找不到可用的卡
        bound.Data!.IsDefault.ShouldBeTrue();
        bound.Data.Last4.ShouldBe("4242");

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Bind_SameTokenTwice_UpdatesInsteadOfDuplicating()
    {
        var first = await BindAsync("pm_test_dup");
        var second = await BindAsync("pm_test_dup");

        first.Succeeded.ShouldBeTrue();
        second.Succeeded.ShouldBeTrue();
        second.Data!.Id.ShouldBe(first.Data!.Id);

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SetDefault_MovesDefaultFlagToSelectedMethod()
    {
        var first = await BindAsync("pm_a");
        var second = await BindAsync("pm_b", setAsDefault: false);

        // 第二张卡显式不设默认时，默认仍是第一张
        second.Data!.IsDefault.ShouldBeFalse();

        var changed = await InScopeAsync<IPaymentMethodService, Result>(
            svc => svc.SetDefaultAsync(UserId, second.Data.Id));
        changed.Succeeded.ShouldBeTrue();

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));

        methods.Data!.Single(m => m.Id == second.Data.Id).IsDefault.ShouldBeTrue();
        methods.Data!.Single(m => m.Id == first.Data!.Id).IsDefault.ShouldBeFalse();
    }

    [Fact]
    public async Task Bind_InsideCallerTransaction_RollbackKeepsTheExistingDefault()
    {
        // ★ 守的是 BindEntityAsync 里「清旧默认」（裸 SQL）前的 EnsureTransactionStartedAsync：
        //   没有前置时清默认逃逸出事务，而新卡随回滚消失——用户一张默认卡都不剩。
        var first = await BindAsync("pm_bind_rb_a");

        using (var scope = ServiceProvider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var svc = scope.ServiceProvider.GetRequiredService<IPaymentMethodService>();

            manager.EnableTransaction();
            var second = await svc.BindAsync(UserId, new BindPaymentMethodDto
            {
                PaymentMethodToken = "pm_bind_rb_b",
                ChannelCode = "Null",
                SetAsDefault = true
            });
            second.Succeeded.ShouldBeTrue(second.Message);
            await manager.RollbackTransactionAsync();
        }

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));

        methods.Data!.Count.ShouldBe(1);
        methods.Data.Single(m => m.Id == first.Data!.Id).IsDefault.ShouldBeTrue();
    }

    /// <summary>
    /// 调用方事务回滚后，被解绑的支付方式必须原样回来（仍有效、仍是默认）。
    /// </summary>
    /// <remarks>
    /// ★ 守的是 <c>RemoveAsync</c> 里那句 <c>EnsureTransactionStartedAsync</c>：
    /// 它保护的是「置卡失效」与下游清理的同生共死。下游清理这一半在本宿主上没有接收方，
    /// 所以这里只能验到卡这一半 —— 完整的两半在
    /// <c>Tnzi.Payment.Subscriptions.Tests</c> 的同名用例里。
    /// </remarks>
    [Fact]
    public async Task Remove_InsideCallerTransaction_RollbackRestoresTheMethod()
    {
        var bound = await BindAsync("pm_remove_rb");
        bound.Succeeded.ShouldBeTrue();

        using (var scope = ServiceProvider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var svc = scope.ServiceProvider.GetRequiredService<IPaymentMethodService>();

            manager.EnableTransaction();
            (await svc.RemoveAsync(UserId, bound.Data!.Id)).Succeeded.ShouldBeTrue();
            await manager.RollbackTransactionAsync();
        }

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.Single(m => m.Id == bound.Data!.Id).IsDefault.ShouldBeTrue();
    }

    [Fact]
    public async Task SetDefault_InsideCallerTransaction_RollbackKeepsTheOldDefault()
    {
        // ★ 守的是 SetDefaultAsync 里 ClearDefaultAsync（裸 SQL）前的 EnsureTransactionStartedAsync：
        //   没有前置时「清旧默认」逃逸出事务，而「设新默认」随回滚撤销——
        //   用户一张默认卡都不剩，后台扣款从此找不到卡，且没有任何报错。
        var first = await BindAsync("pm_rb_a");
        var second = await BindAsync("pm_rb_b", setAsDefault: false);

        using (var scope = ServiceProvider.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            var svc = scope.ServiceProvider.GetRequiredService<IPaymentMethodService>();

            manager.EnableTransaction();
            (await svc.SetDefaultAsync(UserId, second.Data!.Id)).Succeeded.ShouldBeTrue();
            await manager.RollbackTransactionAsync();
        }

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));

        // 回滚后一切如初：旧默认还是默认，而且默认卡恰好一张
        methods.Data!.Single(m => m.Id == first.Data!.Id).IsDefault.ShouldBeTrue();
        methods.Data!.Single(m => m.Id == second.Data!.Id).IsDefault.ShouldBeFalse();
        methods.Data!.Count(m => m.IsDefault).ShouldBe(1);
    }

    [Fact]
    public async Task Bind_WithEmptyToken_IsRejected()
    {
        var bound = await BindAsync(string.Empty);

        bound.Succeeded.ShouldBeFalse();
        bound.Message.ShouldBe(ErrorCodes.PaymentMethodNotFound);
    }

    /// <summary>
    /// 付款人在渠道那边撤销授权（PayPal 撤销 / Stripe 删卡）后，本地必须跟着失效。
    /// </summary>
    /// <remarks>
    /// 不接这条 webhook 也不会立刻出事——下次续费扣款失败照样降级 PastDue 并催款——
    /// 但那要等到下一个计费周期。用户是在渠道那边操作的，多半没意识到自己顺手关掉了这里的自动续费，
    /// 而"续费失败"这个信号迟一个周期到，对订阅业务就是一个周期的收入。
    /// 「顺带清掉订阅快照」那一半在没装续费包的宿主上没有接收方，验证在
    /// <c>Tnzi.Payment.Subscriptions.Tests</c>。
    /// </remarks>
    [Fact]
    public async Task RevocationCallback_DeactivatesTheMethod()
    {
        var bound = await BindAsync("pm_revoked");
        bound.Succeeded.ShouldBeTrue();

        var handled = await SendRevocationCallbackAsync("pm_revoked", "evt-revoke-1");
        handled.Succeeded.ShouldBeTrue();

        var methods = await InScopeAsync<IPaymentMethodService, Result<List<StoredPaymentMethodDto>>>(
            svc => svc.GetUserMethodsAsync(UserId));
        methods.Data!.ShouldBeEmpty();
    }

    /// <summary>
    /// 渠道会重投同一事件，这条路径必须是幂等的。
    /// </summary>
    [Fact]
    public async Task RevocationCallback_IsIdempotentAcrossRedeliveries()
    {
        var bound = await BindAsync("pm_revoked_twice");
        bound.Succeeded.ShouldBeTrue();

        // 用不同的事件ID绕开去重缓存，直击"记录已失效"这条兜底路径
        (await SendRevocationCallbackAsync("pm_revoked_twice", "evt-a")).Succeeded.ShouldBeTrue();
        (await SendRevocationCallbackAsync("pm_revoked_twice", "evt-b")).Succeeded.ShouldBeTrue();

        var stored = await ReloadAsync<StoredPaymentMethod>(bound.Data!.Id);
        stored!.IsActive.ShouldBeFalse();
        stored.IsDefault.ShouldBeFalse();
    }

    /// <summary>
    /// 撤销事件里的凭据不属于本系统时，回 2xx 结束——回失败只会让渠道无休止重投。
    /// </summary>
    [Fact]
    public async Task RevocationCallback_ForUnknownToken_IsAccepted()
    {
        var handled = await SendRevocationCallbackAsync("pm_never_bound_here", "evt-unknown");

        handled.Succeeded.ShouldBeTrue();
    }

    private Task<Result> SendRevocationCallbackAsync(string token, string eventId) =>
        InScopeAsync<IPaymentService, Result>(svc => svc.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Null",
            Parameters = new Dictionary<string, string>
            {
                ["revoked_token"] = token,
                ["event_id"] = eventId
            }
        }));
}
