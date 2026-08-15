namespace Tnzi.Template.Services;

/// <summary>
/// 文件模板的读取服务 —— 消费方读取 <c>.cshtml</c> 模板文件**自述内容**的受支持入口。
/// </summary>
/// <remarks>
/// 模板文件的 front matter 除了引擎要用的 <c>subject</c> / <c>layout</c> 之外，还允许模板作者在
/// <c>metadata:</c> 块里声明任意自定义键（属于哪一组、是否必须纸质签署、取代了哪些旧文档……）。
/// 这些键随文件走，而不是散落在文件看不见的地方。本接口把它们以
/// <see cref="TemplateInfo.Metadata"/> 的形式原样交出，键名由模板作者定义，框架不做约定。
///
/// <para>
/// 与 <see cref="ITemplateStoreService"/> 的分工：Store 服务负责数据库里可编辑的模板行（DB 优先，
/// 文件系统兜底），交出的是实体/DTO；本服务只读文件来源，交出的是文件里的原始声明。
/// 需要"这个模板文件声明了什么"的消费方用本服务，不要去依赖解析器实现类。
/// </para>
///
/// <para>
/// 所有读取都被限制在配置的模板根内（<c>{搜索根}/{Template:TemplateRootPath}</c>），
/// 与渲染引擎的路径校验同口径：引擎渲染得了的文件，本服务才读得到。越界路径返回 <c>null</c>。
/// </para>
/// </remarks>
public interface ITemplateFileService
{
    /// <summary>
    /// 文件系统模板是否启用（<c>Template:EnableFileSystemTemplates</c>）。
    /// 关闭时本服务的所有读取方法都返回空结果 —— 播种一类的调用方应先判断这里，
    /// 否则"配置关着"和"一个模板文件都没有"看起来是一样的。
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 按 模块 + 分类 + 模板名 查找模板文件并解析。
    /// 对应磁盘布局 <c>{TemplateRootPath}/{module}/{category}/{templateName}{扩展名}</c>；
    /// <paramref name="category"/> 为空时退化为 <c>{TemplateRootPath}/{module}/{templateName}{扩展名}</c>。
    /// </summary>
    /// <returns>解析结果；文件不存在、越界或读取失败时返回 <c>null</c>。</returns>
    Task<TemplateInfo?> FindTemplateAsync(string templateName, string module, string? category = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析一个已知路径的模板文件。<paramref name="path"/> 可以是绝对路径，
    /// 也可以是相对模板根的路径（如 <c>Notification/Email/Welcome.cshtml</c>）。
    /// </summary>
    /// <returns>解析结果；文件不存在、落在模板根之外或读取失败时返回 <c>null</c>。</returns>
    Task<TemplateInfo?> ReadTemplateAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 枚举模板根下的全部模板文件并解析。同一 模块/分类/名称 在多个搜索根下重复出现时，
    /// 按搜索根优先级取第一个（与渲染时的解析顺序一致）。
    /// </summary>
    /// <param name="module">按模块过滤（可选，忽略大小写）</param>
    /// <param name="category">按分类过滤（可选，忽略大小写）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<TemplateInfo>> ListTemplatesAsync(string? module = null, string? category = null, CancellationToken cancellationToken = default);
}
