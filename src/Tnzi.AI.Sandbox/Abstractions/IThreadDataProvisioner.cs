namespace Tnzi.AI.Sandbox.Abstractions;

/// <summary>
/// 把一个线程的数据目录布置到磁盘上：建目录、复制技能资源。
/// </summary>
/// <remarks>
/// <para>
/// 由 <c>SandboxMiddleware</c> 发布的 <see cref="SandboxToolEnvironment"/> 在沙箱<b>第一次被用到</b>时调用，
/// 不在中间件入口调用：一次运行里没有工具用到沙箱，磁盘上就什么都不发生。
/// </para>
/// <para>
/// 必须幂等：同一线程每一轮都会再进来一次。
/// </para>
/// </remarks>
public interface IThreadDataProvisioner
{
    /// <summary>
    /// 布置 <paramref name="state"/> 描述的线程目录。
    /// </summary>
    /// <param name="threadId">线程 Id。</param>
    /// <param name="state">线程目录布局（由 <c>ThreadDataMiddleware</c> 算出，可能尚不存在于磁盘）。</param>
    /// <param name="ct">取消令牌。</param>
    Task ProvisionAsync(Guid threadId, ThreadDataState state, CancellationToken ct = default);
}
