namespace Tnzi.Settings;

/// <summary>
/// 参数级：声明「本消费者从这个 Options 里<b>只</b>读下列冷字段」，据此让
/// <c>RuntimeSettingConsumerAuditor</c> 对该 <c>IOptions&lt;T&gt;</c> 注入放行。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要它：审计的判据只能看到「<b>这个 Options 类型</b>里有没有 <c>[RuntimeSetting]</c>」，
/// 而真正的缺陷判据是「<b>这个消费者</b>有没有读到热字段」。两者在同一个 Options 类
/// 同时承载冷热字段时就会分叉 —— 典型形态是部署机密（刻意不做成热设置，不该经管理端下发）
/// 与热设置同住一个类：消费机密的服务在启动时解析一次是<b>正确</b>的，却照样被告警。
/// </para>
/// <para>
/// <b>清单必须穷尽。</b>列出的是消费者读的<b>全部</b>字段，不是「其中几个」。审计只能验证
/// 「列出的这些确实是冷的」，验证不了「没有列漏」—— 少列一个热字段，等于把这条告警关掉。
/// 所以：拿不准读了哪些字段时，不要用这个特性，直接改 <c>IOptionsMonitor&lt;T&gt;</c>。
/// </para>
/// <para>
/// <b>一律用 <c>nameof</c>。</b>写死字符串会在属性改名后静默失配（审计会因此报「没有这个属性」
/// 而不是静默放行，方向是安全的，但没必要）。嵌套字段用点号路径：<c>"Gateway.Timeout"</c>。
/// </para>
/// <para>
/// 声明本身出错（没列字段 / 列了不存在的属性 / 列了带 <c>[RuntimeSetting]</c> 的属性）
/// 一律记<b>告警</b>，且比不加声明时更响：一份说谎的声明关掉的正是这条审计存在的理由。
/// </para>
/// <example>
/// <code>
/// public FileUrlSigner([ReadsOnlyColdSettings(nameof(StorageOptions.UrlSigningKey))] IOptions&lt;StorageOptions&gt; options)
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ReadsOnlyColdSettingsAttribute : Attribute
{
    /// <summary>消费者读到的全部属性名（或点号分隔的嵌套路径）。</summary>
    public IReadOnlyList<string> PropertyPaths { get; }

    /// <remarks>
    /// 刻意不在这里 <c>Check.NotNullOrEmpty</c>：特性构造函数是在审计反射读取时才执行的，
    /// 在这里抛异常会让整条审计炸掉（<c>CustomAttributeFormatException</c>），
    /// 而审计正是要在这种时候还能说话。空清单交由审计判定为「声明无效」并告警。
    /// </remarks>
    public ReadsOnlyColdSettingsAttribute(params string[] propertyPaths)
        => PropertyPaths = propertyPaths ?? [];
}
