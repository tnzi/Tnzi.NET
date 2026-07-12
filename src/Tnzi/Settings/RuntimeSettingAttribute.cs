namespace Tnzi.Settings;

/// <summary>属性级：标记该字段可在 admin 热设置（Global 作用域）。消费方必须用 IOptionsMonitor&lt;T&gt;。</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class RuntimeSettingAttribute : Attribute
{
    public string? Label { get; init; }
    public string? I18n { get; init; }
    public string? Description { get; init; }
    public SettingFieldType Type { get; init; } = SettingFieldType.Auto;
    public bool Required { get; init; }
    public bool ReadOnly { get; init; }
    public double Min { get; init; } = double.NaN;
    public double Max { get; init; } = double.NaN;

    /// <summary>
    /// String/Text 值的正则约束（.NET 语法，整值匹配）。前端保存前与后端写入时同源校验；
    /// 无效正则在启动期定义提取时抛出（fail-fast）。仅对 String/Text 生效。
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>Select 候选值，逗号分隔；枚举类型可省略（自动用枚举名）。</summary>
    public string? Options { get; init; }

    /// <summary>
    /// 组内二级分节标签（纯展示层）。同一 Subsection 的字段在前端聚合成可折叠小节；
    /// 未设置的字段渲染在小节之前的默认区。不影响存储/校验/热链路，仅用于长配置组的视觉分组。
    /// </summary>
    public string? Subsection { get; init; }
}
