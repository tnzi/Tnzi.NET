namespace Tnzi.Settings;

/// <summary>声明 Options 类绑定的配置 section（单一事实源）。值可为嵌套路径，如 "System:Encryption"。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ConfigSectionAttribute(string section) : Attribute
{
    public string Section { get; } = Check.NotNullOrWhiteSpace(section);
}
