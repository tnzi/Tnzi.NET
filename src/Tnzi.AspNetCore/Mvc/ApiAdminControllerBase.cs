
namespace Tnzi.AspNetCore.Mvc;

/// <summary>
/// 管理类API控制器基类
/// 提供统一的管理类API授权和Swagger分组。
/// 基类只要求"已认证"(裸 <c>[ApiAuthorize]</c>),不再压 <c>Admin.Manage</c> 码:
/// 每个 admin 控制器以类级模块 <c>.view</c> 码 + 写端点方法级操作码承担真实门禁
/// (AND 语义),"能否进后台"由用户是否持有任何具体授权自然决定。这样进门/落脚点
/// 不是权限矩阵里可被清空的行——把某角色权限清零后,其成员登录只会得到一个空壳
/// 后台(菜单只剩公共项、所有业务 API 403),而不是连权限自查(access-profile)
/// 都被锁死的死循环。
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "admin")]
[ApiAuthorize]
[StableApi(Since = "0.1.0")]
public abstract class ApiAdminControllerBase : ApiControllerBase
{
    /// <summary>
    /// 初始化管理类API控制器基类
    /// </summary>
    /// <param name="serviceProvider">服务提供者（可选）</param>
    protected ApiAdminControllerBase(IServiceProvider? serviceProvider = null)
        : base(serviceProvider)
    {
    }

    /// <summary>
    /// 把服务层导出的 CSV 内容包装为文件下载响应：UTF-8 BOM(Excel 直开不乱码) + text/csv + 时间戳文件名。
    /// 失败时按 Result 状态码返回 ApiResult 信封(前端 download 客户端解析错误 body 提取 message)。
    /// CSV 内容生成 MUST 经 <see cref="Tnzi.Utilities.CsvBuilder"/>(公式注入防护),禁止手写转义。
    /// </summary>
    /// <param name="result">服务层导出结果(Data 为完整 CSV 文本)</param>
    /// <param name="baseName">文件名前缀(实际文件名为 {baseName}_{UTC 时间戳}.csv)</param>
    protected IActionResult CsvFile(Result<string> result, string baseName)
    {
        Check.NotNull(result);
        Check.NotNullOrWhiteSpace(baseName);

        if (!result.Succeeded)
            return StatusCode(result.Code ?? 400, result.ToApiResult());

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(result.Data ?? string.Empty);
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return File(bytes, "text/csv", $"{baseName}_{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }
}