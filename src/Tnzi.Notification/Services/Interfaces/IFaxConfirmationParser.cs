namespace Tnzi.Notification.Services;

/// <summary>
/// 判读一封邮件是不是传真回执，是的话读出结论。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>这是整条回执链上唯一必须能被换掉的一环</b>：email-to-fax 网关的回执格式没有任何标准，
/// 每一家的主题措辞、正文排版、要不要回复原邮件都不一样。框架给一个通用的启发式实现
/// （<see cref="HeuristicFaxConfirmationParser"/>）作为开箱即用的默认值，
/// 消费应用照自家网关的格式注册一个就整体覆盖 —— 那比让框架去猜准得多。
/// </para>
/// <para>
/// <b>读不懂就返回 <c>null</c>。</b>收件箱里本来就有别的信（人回的、系统通知、垃圾邮件），
/// 判读不出来是常态而不是异常。返回 <c>null</c> 的邮件不会引起任何动作。
/// </para>
/// </remarks>
public interface IFaxConfirmationParser
{
    /// <summary>
    /// 判读一封邮件。
    /// </summary>
    /// <param name="message">收到的邮件。</param>
    /// <returns>读出的结论；这封信不是传真回执、或读不出结论时为 <c>null</c>。</returns>
    FaxConfirmation? Parse(FaxConfirmationMessage message);
}
