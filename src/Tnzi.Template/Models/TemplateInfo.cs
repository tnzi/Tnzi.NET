namespace Tnzi.Template.Models;

/// <summary>
/// 通用模板信息（独立于业务实体）
/// </summary>
public class TemplateInfo
{
    /// <summary>
    /// 模板名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 所属模块。来源于文件系统时由模板根下的第一段目录名推导
    /// （<c>{TemplateRootPath}/{module}/{category}/{name}.cshtml</c>）。
    /// </summary>
    public string? Module { get; set; }

    /// <summary>
    /// 模板分类（如 Email/SMS/Push 或模块自定义类别）
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// front matter 中声明的 <c>description</c>
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 主题模板内容
    /// </summary>
    public string SubjectTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 主体内容模板
    /// </summary>
    public string ContentTemplate { get; set; } = string.Empty;

    /// <summary>
    /// 默认布局名称
    /// </summary>
    public string? DefaultLayoutName { get; set; }

    /// <summary>
    /// 模板文件在 front matter 的 <c>metadata:</c> 块里自述的扩展键值。
    /// 键完全由模板作者定义，框架不做任何约定，也不参与校验。
    /// <para>
    /// 标量值一律是 <see cref="string"/>：YAML 里的 <c>true</c> / <c>42</c> 不带类型信息，
    /// 不会被推断成 <see cref="bool"/> / <see cref="int"/>，需要类型的消费方自己转。
    /// 嵌套结构分别是 <c>List&lt;object&gt;</c> 与 <c>Dictionary&lt;object, object&gt;</c>。
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// ---
    /// subject: Monthly Statement
    /// metadata:
    ///   group: billing
    ///   printOnly: true
    /// ---
    /// </code>
    /// </example>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// 文件路径（如果来源于文件系统）
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// 文件最后修改时间（如果来源于文件系统）
    /// </summary>
    public DateTime? LastModified { get; set; }
}

