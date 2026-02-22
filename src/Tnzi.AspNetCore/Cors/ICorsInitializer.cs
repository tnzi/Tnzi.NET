
namespace Tnzi.AspNetCore.Cors;

/// <summary>
/// CORS 初始化器接口
/// </summary>
public interface ICorsInitializer
{
    /// <summary>
    /// 添加 CORS 配置
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    IServiceCollection AddCors(IServiceCollection services);

    /// <summary>
    /// 应用 CORS 中间件
    /// </summary>
    /// <param name="app">应用构建器</param>
    /// <returns>应用构建器</returns>
    IApplicationBuilder UseCors(IApplicationBuilder app);
}