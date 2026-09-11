using Tnzi.Hangfire.Controllers.Admin;
using Tnzi.Hangfire.Options;

namespace Tnzi.Hangfire.Tests;

/// <summary>
/// Hangfire 关掉时，计划任务管理端点该怎么回答。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：四个端点都直接读静态的 <c>JobStorage.Current</c>。
/// <c>Hangfire:Enabled = false</c> 时模块不配置任何存储，那个静态属性会抛
/// "JobStorage.Current property value has not been initialized" —— 每个请求都是 <b>500</b>。
/// </para>
/// <para>
/// ★ 控制器此前<b>没有任何构造依赖</b>，所以 <c>ConditionalControllerProvider</c> 也救不了它：
/// 那个机制按「依赖解析不出来」抑制控制器，而这里所有依赖都解析得出，只是功能被关了。
/// 按框架约定答 <b>501</b> —— 不是 503：503 是暂时性故障，会让监控与客户端一直重试一件永远不会好的事。
/// </para>
/// </remarks>
public class ScheduledJobAdminControllerTests
{
    [Fact]
    public void GetList_WhenHangfireIsDisabled_AnswersNotImplemented()
    {
        var result = CreateController(enabled: false).GetList();

        Assert.False(result.Success);
        Assert.Equal(501, result.Code);
    }

    [Fact]
    public void Get_WhenHangfireIsDisabled_AnswersNotImplemented()
    {
        var result = CreateController(enabled: false).Get("some-job");

        Assert.Equal(501, result.Code);
    }

    /// <summary>
    /// 关掉时的答复必须优先于参数校验：否则 <c>id</c> 为空会先答 400，
    /// 把「这个功能没开」说成「你的请求写错了」。
    /// </summary>
    [Fact]
    public void Trigger_WhenHangfireIsDisabled_AnswersNotImplementedEvenForABlankId()
    {
        var result = CreateController(enabled: false).Trigger(string.Empty);

        Assert.Equal(501, result.Code);
    }

    [Fact]
    public void Delete_WhenHangfireIsDisabled_AnswersNotImplemented()
    {
        var result = CreateController(enabled: false).Delete("some-job");

        Assert.Equal(501, result.Code);
    }

    /// <summary>
    /// 启用时不得走 501 那条路 —— 防止把守卫做成"永远不可用"。
    /// </summary>
    /// <remarks>
    /// 这里断言的是 <b>400</b>：参数校验说明请求确实进入了正常处理路径，
    /// 而空 id 让它在碰到 <c>JobStorage.Current</c>（测试环境里没有存储）之前就返回了。
    /// </remarks>
    [Fact]
    public void Trigger_WhenHangfireIsEnabled_FallsThroughToTheOrdinaryValidation()
    {
        var result = CreateController(enabled: true).Trigger(string.Empty);

        Assert.Equal(400, result.Code);
    }

    private static DefaultScheduledJobAdminController CreateController(bool enabled)
        => new(Microsoft.Extensions.Options.Options.Create(new HangfireOptions { Enabled = enabled }));
}
