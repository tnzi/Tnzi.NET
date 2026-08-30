namespace Tnzi.Payment.Services;

/// <summary>
/// 一条挂在支付后台循环上的定期扫描。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么存在</b>：<see cref="PaymentBackgroundService"/> 拆分前在一轮开始时**一次性**
/// <c>GetRequiredService</c> 出支付 / 退款 / 订阅三个服务，然后才逐条跑扫描。
/// 三个解析都在 <c>try</c> 之外、却被最外层那个「记一条 Error 就继续下一轮」的
/// <c>catch (Exception)</c> 罩着 —— 于是只要<b>任何一个</b>服务解析不出来，
/// 这一轮的<b>全部</b>扫描都不会执行，包括与它毫不相干的「关闭过期支付」和「对账在途退款」。
/// 症状是「后台任务安静地什么都不做」，日志里只有一句看不出因果的解析异常。
/// </para>
/// <para>
/// 这是一个<b>独立于本次拆分</b>就该修的缺陷；拆分只是让它从「理论上」变成「必然」——
/// 订阅服务住进可选包之后，不加载它的宿主每一轮都会走进这条路径。
/// 修法是把「解析」搬进各自的 <c>try</c>，并让可选域用本契约<b>贡献</b>自己的扫描，
/// 而不是由后台服务去认识它们。
/// </para>
/// <para>
/// 以 <c>IEnumerable&lt;T&gt;</c> 解析：没有任何实现就是空集合，父模块自己那两条扫描照跑不误。
/// 少一项能力，不是一次停摆。
/// </para>
/// </remarks>
public interface IPaymentScheduledScan
{
    /// <summary>
    /// 扫描名，只用于日志。用人话写（如 <c>renew due subscriptions</c>），
    /// 它是运维在日志里唯一能看到的标识。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 跑一轮，返回本轮处理的条数。
    /// </summary>
    /// <remarks>
    /// 抛异常是允许的：调用方逐条隔离，一条扫描失败不影响同一轮里的其它扫描。
    /// </remarks>
    Task<Result<int>> RunAsync(CancellationToken cancellationToken = default);
}
