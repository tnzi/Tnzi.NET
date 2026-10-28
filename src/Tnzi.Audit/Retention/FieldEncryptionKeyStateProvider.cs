namespace Tnzi.Audit.Retention;

/// <summary>
/// 默认的密钥存活判定：回查 <c>Tnzi.EFCore</c> 字段级加密的配置密钥环。
/// </summary>
/// <remarks>
/// <para>
/// 判据是「该密钥标识已不在密钥环里」。字段级加密的加密删除正是这么做的：
/// 把一把密钥移出密钥环，用它加密的数据就永久不可读。
/// </para>
/// <para>
/// <strong>字段级加密未启用时恒返回 <c>false</c>。</strong>密钥环本来就是空的，
/// 据此判定「已销毁」等于盖一个没资格盖的章。这是刻意的保守，不是遗漏。
/// 密钥不在框架配置里的部署（云端密钥管理服务、自有硬件模块，或加密根本发生在客户端），
/// 应当注册自己的 <see cref="IEncryptionKeyStateProvider"/>。
/// </para>
/// </remarks>
public sealed class FieldEncryptionKeyStateProvider : IEncryptionKeyStateProvider
{
    private readonly IOptionsMonitor<FieldEncryptionOptions>? _encryptionOptions;
    private readonly FieldEncryptionKeyRing? _keyRing;

    /// <summary>
    /// 初始化一个 <see cref="FieldEncryptionKeyStateProvider"/> 类型的新实例。
    /// </summary>
    /// <param name="encryptionOptions">
    /// 字段级加密选项。可为 <c>null</c>：不加载该能力的应用照常工作，判定恒为「未销毁」。
    /// </param>
    /// <param name="keyRing">
    /// 密钥环(配置密钥与密钥文件合并后的视图)。可为 <c>null</c>:此时只看配置里的 <c>Keys</c>,
    /// 密钥文件里的密钥会被误判为「已销毁」,所以加载了 <c>Tnzi.EFCore</c> 的应用应当让容器提供它。
    /// </param>
    public FieldEncryptionKeyStateProvider(
        IOptionsMonitor<FieldEncryptionOptions>? encryptionOptions = null,
        FieldEncryptionKeyRing? keyRing = null)
    {
        _encryptionOptions = encryptionOptions;
        _keyRing = keyRing;
    }

    /// <inheritdoc />
    public bool IsDestroyed(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            return false;
        }

        var encryption = _encryptionOptions?.CurrentValue;
        if (encryption is not { Enabled: true })
        {
            return false;
        }

        if (_keyRing is not null)
        {
            // 密钥文件读不出来时这里抛 FieldEncryptionException:无法确认,由调用方按契约降级为「未销毁」并记 Warning。
            return !_keyRing.Contains(keyId);
        }

        return !encryption.Keys.ContainsKey(keyId);
    }
}
