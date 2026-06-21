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
    /// <summary>Select 候选值，逗号分隔；枚举类型可省略（自动用枚举名）。</summary>
    public string? Options { get; init; }
}
