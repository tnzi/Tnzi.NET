namespace Tnzi.Payment.Options;

/// <summary>
/// 本实例是否运行支付后台扫描。配置路径：<c>Payment:BackgroundTasks</c>。
/// </summary>
/// <remarks>
/// <para>
/// 后台扫描（续费扣款、试用转正、逾期过期 / 暂停服务、过期支付关闭、在途退款对账，以及各可选域贡献的扫描）
/// 在每个运行着的实例里都跑。几个实例共用一个库、却不是同一套部署时（开发机连着生产库），
/// 开发机上的那一份会以它自己的渠道凭据扣真实订阅，扣不成就把客户推进逾期、暂停。
/// 在那些实例上配 <c>Payment:BackgroundTasks:Enabled=false</c>，扫描就只在该跑的实例上跑。
/// </para>
/// <para>
/// ★ <b>刻意不是运行期设置</b>（没有 <c>[RuntimeSetting]</c>，只在启动时读一次）：运行期设置存在共用的设置表里，
/// 在一台机器上关掉就是在所有共用这张表的实例上关掉，包括生产，那正是这个开关要避免的。
/// 它属于「这一个进程」，只能来自本实例自己的配置（appsettings、环境变量 <c>Payment__BackgroundTasks__Enabled</c>）。
/// </para>
/// <para>只管定时扫描。回调、当场补缴、用户发起的付款与退款这些按需路径不受影响。</para>
/// </remarks>
[ConfigSection("Payment:BackgroundTasks")]
public class PaymentBackgroundTaskOptions
{
    /// <summary>
    /// 是否在本实例运行支付后台扫描，默认 true。
    /// </summary>
    public bool Enabled { get; set; } = true;
}
