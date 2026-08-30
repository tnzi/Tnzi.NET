namespace Tnzi.Storage.Services;

/// <summary>
/// 清理任务的扩展点：让**别的程序集**把自己那张表的过期数据挂进同一次后台清理里。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由是 <see cref="FileCleanupService"/> 曾经直接吃 <c>FileUploadSession</c> 与
/// <c>FileChunk</c> 两个仓储，而这两张表属于工作区子模块。子模块不加载时，一个
/// <b>无条件注册的托管服务</b>会解析不出构造参数 —— 那不是「少一项能力」，而是宿主启动即崩。
/// 改成契约之后，缺席的表现退化成「这一趟少扫了一类数据」。
/// </para>
/// <para>
/// ★ <b>必须以 <see cref="IEnumerable{T}"/> 注入，不能用 <c>T?</c> 或 <c>IReadOnlyList&lt;T&gt;</c></b>：
/// 微软的内置容器只对 <c>IEnumerable&lt;T&gt;</c> 做「无人注册就给空集合」的特判。
/// 换成另外两种写法，没有任何实现时同样解析失败，等于把刚拆掉的那颗地雷原样埋回去。
/// </para>
/// <para>
/// 实现方自己负责租户隔离：清理跑在没有 <c>HttpContext</c> 的作用域里，当前租户通常为空。
/// 单次上限由调用方给（<c>Storage:Cleanup:MaxFilesPerRun</c>），与父模块自己的几趟清理同源。
/// </para>
/// </remarks>
public interface IStorageCleanupContributor
{
    /// <summary>
    /// 这一趟扫的是什么，用于日志。取一个人读得懂的短名（如 <c>"ExpiredUploadSessions"</c>）。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 执行一趟清理，返回删掉的条数。
    /// </summary>
    /// <param name="maxItems">本次允许处理的最大条数，防止单趟长时间占用连接。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 抛出的异常由调用方逐个捕获并记进 <see cref="CleanupResult.Errors"/>：
    /// 一个贡献者失败不该让其它贡献者与父模块自己的几趟清理跟着停摆。
    /// </remarks>
    Task<int> CleanupAsync(int maxItems, CancellationToken cancellationToken = default);
}
