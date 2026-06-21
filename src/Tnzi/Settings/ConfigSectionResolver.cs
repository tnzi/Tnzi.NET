namespace Tnzi.Settings;

/// <summary>从 Options 类型解析配置 section：优先 [ConfigSection]，否则类型名去 "Options" 后缀。</summary>
public static class ConfigSectionResolver
{
    public static string Resolve(Type optionsType)
    {
        Check.NotNull(optionsType);
        var attr = optionsType.GetCustomAttribute<ConfigSectionAttribute>();
        if (attr != null) return attr.Section;
        var name = optionsType.Name;
        return name.EndsWith("Options", StringComparison.Ordinal) ? name[..^"Options".Length] : name;
    }
}
