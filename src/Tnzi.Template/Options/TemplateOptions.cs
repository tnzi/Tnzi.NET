namespace Tnzi.Template.Options;

/// <summary>
/// 模板引擎配置选项
/// </summary>
public class TemplateOptions
{
    /// <summary>
    /// 模板根目录（主搜索路径，通常是应用层的 Templates 目录）
    /// </summary>
    public string TemplateRootPath { get; set; } = "Templates";

    /// <summary>
    /// 额外的搜索路径（按优先级从高到低排列）
    /// 典型用法：Host 模块提供默认模板路径作为 fallback
    /// 查找顺序：TemplateRootPath → AdditionalSearchPaths[0] → AdditionalSearchPaths[1] → ...
    /// </summary>
    public List<string> AdditionalSearchPaths { get; set; } = new();

    /// <summary>
    /// 是否启用文件系统模板
    /// </summary>
    public bool EnableFileSystemTemplates { get; set; } = true;

    /// <summary>
    /// 默认布局模板路径
    /// </summary>
    public string? DefaultLayoutPath { get; set; } = "_Layout.cshtml";
    
    /// <summary>
    /// 是否启用模板缓存
    /// </summary>
    public bool EnableCache { get; set; } = true;
    
    /// <summary>
    /// 缓存过期时间（秒）
    /// </summary>
    public int CacheExpirationSeconds { get; set; } = 3600;
    
    /// <summary>
    /// 是否启用热重载（仅开发环境）
    /// </summary>
    public bool EnableHotReload { get; set; } = false;
    
    /// <summary>
    /// 模板文件扩展名
    /// </summary>
    public string TemplateExtension { get; set; } = ".cshtml";
    
    /// <summary>
    /// 是否启用调试模式（输出编译错误详情）
    /// </summary>
    public bool EnableDebug { get; set; } = false;

    /// <summary>
    /// 缓存大小限制（最多缓存多少个模板）
    /// </summary>
    public int CacheSizeLimit { get; set; } = 1000;
}

