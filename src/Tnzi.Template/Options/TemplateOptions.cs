namespace Tnzi.Template.Options;

/// <summary>
/// 模板引擎配置选项
/// </summary>
public class TemplateOptions
{
    /// <summary>
    /// 模板根目录（主模板根，通常是应用层的 Templates 目录）。
    /// 相对路径基于 <c>AppContext.BaseDirectory</c>（模板文件经 CopyToOutputDirectory 复制到的位置）；
    /// 开发期还会再看一眼 <c>ContentRootPath</c> 下的同名目录。
    /// </summary>
    public string TemplateRootPath { get; set; } = "Templates";

    /// <summary>
    /// 额外的模板根（按优先级从高到低排列）。
    /// <b>每一项都是一个完整的模板根</b>，与 <see cref="TemplateRootPath"/> 同级：它下面直接放
    /// <c>{module}/{category}/{name}.cshtml</c> 与 <c>Layouts/{category}/_{name}.cshtml</c>，
    /// <b>不会</b>再在它下面拼 <see cref="TemplateRootPath"/>。要共享 <c>D:/shared/Templates</c> 就配它本身，不是 <c>D:/shared</c>。
    /// 相对项基于 <c>AppContext.BaseDirectory</c> 解析。
    /// 查找顺序：TemplateRootPath → AdditionalSearchPaths[0] → AdditionalSearchPaths[1] → ...
    /// 启动时 <c>TemplateOptionsPostConfigure</c> 会把每个业务模块程序集旁的 <c>Templates</c> 目录追加进来（插件式分目录部署）。
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
    /// 缓存大小限制（最多缓存多少个模板；0 或负数表示不限制）。
    /// 只作用于模板引擎自己的缓存实例，不影响全进程共享的 <c>IMemoryCache</c>（那个由 <c>Caching:MemorySizeLimit</c> 管）。
    /// </summary>
    public int CacheSizeLimit { get; set; } = 1000;
}

