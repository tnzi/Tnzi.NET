namespace Tnzi.Utilities;

/// <summary>
/// 本进程实例的身份。
/// </summary>
/// <remarks>
/// <para>
/// 多实例部署里，「哪个实例发的」「每个实例各收一份」这类问题都需要一个进程级的标识。
/// 它在进程启动时生成一次、进程结束即失效 —— 刻意<b>不</b>持久化：重启后的进程是一个新实例，
/// 旧实例名下的东西（例如代理上的 per-instance 队列）随连接断开一起消失才是对的。
/// </para>
/// <para>
/// 集中放在核心而不是让每个需要它的模块各自 <c>Guid.NewGuid()</c>：两个模块各持一份就会出现
/// 「同一个进程在 A 眼里是实例 1、在 B 眼里是实例 2」，回环判定与队列命名对不上号。
/// </para>
/// </remarks>
[StableApi(Since = "0.1.0")]
public static class TnziInstance
{
    /// <summary>
    /// 本进程实例的标识（进程生命周期内不变，重启即换）。
    /// </summary>
    public static Guid Id { get; } = Guid.NewGuid();
}
