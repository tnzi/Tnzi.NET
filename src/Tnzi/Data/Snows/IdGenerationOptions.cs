namespace Tnzi.Data.Snows;

/// <summary>
/// 雪花 ID 生成器配置，绑定 <c>IdGeneration</c> 配置节。
/// </summary>
/// <remarks>
/// <para>
/// ★ <see cref="WorkerId"/> 是<b>部署参数</b>，不是可选微调：雪花 id 的唯一性建立在「同一毫秒内每个进程
/// 的机器码不同」之上，两个副本共用一个 WorkerId 就会在同一毫秒各自产出完全相同的 long ——
/// long 主键插入冲突（随机 500，只在多实例下复现），Payment 的交易号 / 发票号这类没有唯一约束的列
/// 则静默重号。此前全仓没有任何配置入口，<c>IdHelper.NextId</c> 静默回退 WorkerId=1。
/// </para>
/// <para>
/// 失败方向：未配置（既没给 <see cref="WorkerId"/>、也没能从主机名派生）时，
/// <c>CoreServicesModule</c> 在 <b>Production</b> 环境启动即失败并指名要配的键，其它环境记 Warning 用默认值 1。
/// 环境判据是宿主的 <c>IHostEnvironment</c>；拿不到宿主环境（裸 ServiceCollection 的单元测试 / 脚本）只警告不拦。
/// </para>
/// <para>
/// 多实例部署的两种给法：① 每个副本经环境变量 <c>IdGeneration__WorkerId</c> 给不同的值；
/// ② K8s StatefulSet 一类主机名带稳定序号的部署开 <see cref="WorkerIdFromHostname"/>，
/// 由主机名末尾的数字派生（序号 + 1，因为 WorkerId 0 不是合法机器码，生成器一律拒绝）。
/// </para>
/// </remarks>
[ConfigSection("IdGeneration")]
public class IdGenerationOptions
{
    /// <summary>
    /// 机器码。范围 [1, 2^<see cref="WorkerIdBitLength"/> - 1]，每个运行副本必须不同。
    /// 显式给出时优先于 <see cref="WorkerIdFromHostname"/>。
    /// </summary>
    public ushort? WorkerId { get; set; }

    /// <summary>
    /// 从主机名末尾的数字派生机器码（K8s StatefulSet 的 <c>api-0</c> / <c>api-1</c> …）：WorkerId = 序号 + 1。
    /// 主机名末尾没有数字时视同未配置。
    /// </summary>
    public bool WorkerIdFromHostname { get; set; }

    /// <summary>
    /// 机器码位长，范围 1-21，默认 6（最多 63 个副本）。与 <see cref="SeqBitLength"/> 之和不超过 22。
    /// </summary>
    public byte WorkerIdBitLength { get; set; } = 6;

    /// <summary>
    /// 序列数位长，范围 2-21，默认 6。与 <see cref="WorkerIdBitLength"/> 之和不超过 22。
    /// </summary>
    public byte SeqBitLength { get; set; } = 6;

    /// <summary>
    /// 该位长下允许的最大机器码。
    /// </summary>
    public int MaxWorkerId => (1 << WorkerIdBitLength) - 1;
}
