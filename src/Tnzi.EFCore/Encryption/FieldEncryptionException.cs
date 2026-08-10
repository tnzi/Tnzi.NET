namespace Tnzi.EFCore.Encryption;

/// <summary>
/// 字段级加密/解密失败。
/// </summary>
/// <remarks>
/// 继承 <see cref="InfrastructureException"/>，会被框架的异常中间件归入基础设施类错误，
/// 不把加密细节泄露给终端用户。
/// <para>
/// <strong>解密失败一律抛出，绝不回退成「当作明文返回」。</strong>
/// 一旦回退，被篡改的密文会被当成正常业务数据流入下游，且没有任何告警，
/// 这比抛异常危险得多。
/// </para>
/// </remarks>
public class FieldEncryptionException : InfrastructureException
{
    /// <summary>基础设施组件名。</summary>
    private const string Component = "FieldEncryption";

    /// <summary>
    /// 失败是否源于「密文所引用的密钥不在当前密钥环中」。
    /// </summary>
    /// <remarks>
    /// 值得与「密文损坏」分开，因为两者的处置完全不同：
    /// 密钥缺失可能是<strong>加密删除的正常结果</strong>（数据已按保留策略销毁，
    /// 密文留在原地但永久不可读），也可能是运维漏配了历史密钥；
    /// 而密文损坏意味着有人动过数据，属于安全事件。
    /// </remarks>
    public bool IsKeyMissing { get; }

    /// <summary>
    /// 初始化 <see cref="FieldEncryptionException"/>。
    /// </summary>
    /// <param name="message">错误消息（英文，面向运维日志，不含明文或密钥材料）。</param>
    /// <param name="isKeyMissing">是否为密钥缺失导致。</param>
    /// <param name="innerException">内部异常。</param>
    public FieldEncryptionException(string message, bool isKeyMissing = false, Exception? innerException = null)
        : base(Component, message, isRetryable: false, innerException)
    {
        IsKeyMissing = isKeyMissing;
    }
}
