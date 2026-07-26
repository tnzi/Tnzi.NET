namespace Tnzi.Settings;

/// <summary>
/// 配置中心字段类型。决定前端渲染控件与后端值校验方式。
/// 注意：不提供列表类型 - .NET 配置绑定要求列表用 Key:0/Key:1 索引键，
/// 单条 Setting 行无法绑定 List&lt;T&gt;；列表/复杂配置请用自定义分区组件。
/// </summary>
// 无 [ExperimentalApi]：该 attribute 的 AttributeUsage 不含 Enum（CS0592）；
// 实验性边界由消费它的 SettingFieldDefinition 标注承载。
public enum SettingFieldType
{
    String = 0,

    /// <summary>多行文本（textarea）。单行字符串用 <see cref="String"/>。</summary>
    Text = 1,
    Int = 2,
    Decimal = 3,
    Boolean = 4,
    Select = 5,
    Password = 6,

    /// <summary>仅用于 [RuntimeSetting]：由属性 CLR 类型推断实际类型，不作为最终 SettingFieldDefinition.Type。</summary>
    Auto = 7,

    /// <summary>
    /// 时长（<see cref="System.TimeSpan"/>）。值以 canonical 字符串存取（如 "00:05:00" / "1.12:00:00"），
    /// 必须 <see cref="System.TimeSpan.TryParse(string, out System.TimeSpan)"/> 可逆，保证热更新写回后
    /// ConfigurationBinder 原生绑定不失败。前端渲染带格式提示的文本输入。
    /// </summary>
    Duration = 8
}
