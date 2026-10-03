namespace Tnzi.Storage.Services;

/// <summary>
/// 缩略图一侧的 PDF 渲染闸门：同一时刻至多一次渲染在跑，调用方等位置加等渲染合计不超过给定时限。
/// </summary>
/// <remarks>
/// <para>
/// PDF 首页缩略图在上传请求里生成，而 <see cref="IPdfRasterizer"/> 的默认实现（PDFium）是同步的：
/// 它在一把进程级的锁里执行、被包成已完成的 <see cref="Task"/> 返回、中途取消不掉，渲染多久完全由文件决定。
/// 直接 <c>await</c> 它，一份首页矢量操作极多的 PDF 就能把上传请求挂上几分钟，而同时进来的每一份 PDF 上传
/// 各占一个线程池线程在那把锁上排队。
/// </para>
/// <para>
/// 这里做三件事：①渲染挪到线程池上跑，调用方只是异步地等它；②一个位置的信号量让同一时刻只有一个线程
/// 被 PDFium 占住，其余调用方<b>异步</b>排队，不占线程；③时限一到就放弃结果返回。被放弃的那次渲染取消不掉，
/// 它会在后台跑完并在跑完时才让出位置 —— 所以超时之后的调用方多半会在等位置时就超时、直接不画，
/// 这正是想要的：宁可暂时没有缩略图，也不让上传跟着一份坏文件一起挂住。
/// </para>
/// <para>
/// 它只管本模块自己的渲染调用。别的模块直接调光栅化器时仍会在 PDFium 的锁上等被放弃的那次渲染跑完 ——
/// 要彻底隔离一份文件的渲染时长只能把它放进独立进程，那是光栅化器实现层面的事。
/// </para>
/// <para>
/// 模块注册为单例；它必须在整个进程里是同一个，否则「同一时刻只有一次渲染」不成立。
/// </para>
/// </remarks>
public sealed class PdfThumbnailRenderGate
{
    private readonly SemaphoreSlim _slot = new(1, 1);

    /// <summary>
    /// 在闸门内执行一次渲染。
    /// </summary>
    /// <typeparam name="T">渲染结果类型。</typeparam>
    /// <param name="render">渲染动作；会在线程池上执行，允许它同步阻塞。</param>
    /// <param name="budget">等位置加等渲染的总时限。</param>
    /// <param name="cancellationToken">调用方取消；取消只让调用方不再等，已经开始的渲染照样跑完。</param>
    /// <returns>
    /// <c>Completed</c> 为 <c>false</c> 表示时限内没拿到结果（没轮到或没渲染完）；为 <c>true</c> 时 <c>Value</c> 是渲染结果。
    /// 渲染本身抛出的异常原样抛给调用方。
    /// </returns>
    public async Task<(bool Completed, T? Value)> RunAsync<T>(Func<Task<T>> render, TimeSpan budget, CancellationToken cancellationToken = default)
        where T : class
    {
        Check.NotNull(render);
        if (budget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "The render budget must be positive.");

        var startedAt = Environment.TickCount64;
        if (!await _slot.WaitAsync(budget, cancellationToken))
            return (false, null);

        Task<T> work;
        try
        {
            work = Task.Run(render, CancellationToken.None);
        }
        catch
        {
            _slot.Release();
            throw;
        }

        // 位置在渲染真正结束时才让出，而不是在调用方放弃等待时：被放弃的渲染仍占着 PDFium。
        // 同时观察掉它的异常 —— 没人再 await 一个被放弃的任务。
        _ = work.ContinueWith(
            static (finished, state) =>
            {
                _ = finished.Exception;
                ((SemaphoreSlim)state!).Release();
            },
            _slot,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var remaining = budget - TimeSpan.FromMilliseconds(Environment.TickCount64 - startedAt);
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        try
        {
            return (true, await work.WaitAsync(remaining, cancellationToken));
        }
        catch (TimeoutException) when (!work.IsCompleted)
        {
            return (false, null);
        }
    }
}
