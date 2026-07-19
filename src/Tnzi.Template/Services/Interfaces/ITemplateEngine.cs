namespace Tnzi.Template.Services;

/// <summary>
/// 模板引擎接口
/// </summary>
public interface ITemplateEngine
{
    /// <summary>
    /// 从字符串渲染模板
    /// </summary>
    /// <param name="templateContent">模板内容</param>
    /// <param name="model">模板模型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染后的内容</returns>
    Task<string> RenderAsync(string templateContent, object? model = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从文件渲染模板
    /// </summary>
    /// <param name="filePath">模板文件路径</param>
    /// <param name="model">模板模型</param>
    /// <param name="layoutPath">布局文件路径（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染后的内容</returns>
    Task<string> RenderFromFileAsync(string filePath, object? model = null, string? layoutPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从模板名称渲染（从模板存储加载）
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <param name="model">模板模型</param>
    /// <param name="layoutName">布局名称（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染后的内容</returns>
    Task<string> RenderFromNameAsync(string templateName, object? model = null, string? layoutName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证模板语法
    /// </summary>
    /// <param name="templateContent">模板内容</param>
    /// <returns>验证结果（包含是否通过和错误列表）</returns>
    Task<TemplateValidationResult> ValidateAsync(string templateContent);

    /// <summary>
    /// 预编译模板（应用启动时调用，提升首次渲染性能）
    /// </summary>
    /// <param name="templatePaths">模板路径列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task PrecompileTemplatesAsync(IEnumerable<string> templatePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除模板缓存
    /// </summary>
    void ClearCache();
}
