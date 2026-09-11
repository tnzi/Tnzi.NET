using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Models;
using Tnzi.AspNetCore.Mvc;

namespace Tnzi.AI.Sandbox.Controllers.Admin;

/// <summary>
/// Sandbox 管理控制器 - 查询沙箱配置和 Provider 状态
/// </summary>
[DefaultController]
[Route("admin/sandbox")]
[ApiAuthorize(PermissionName = "ai.sandbox.view")]
public class DefaultSandboxAdminController : ApiAdminControllerBase
{
    private readonly ISandboxProvider _provider;
    private readonly IOptions<SandboxModuleOptions> _options;

    public DefaultSandboxAdminController(ISandboxProvider provider, IOptions<SandboxModuleOptions> options)
    {
        _provider = Check.NotNull(provider);
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// 获取沙箱模块状态和配置摘要
    /// </summary>
    [HttpGet("status")]
    public virtual ApiResult<SandboxStatusDto> GetStatus()
    {
        var opts = _options.Value;

        // ★ 按<b>当前生效的 provider</b> 投影，而不是恒读 Local。此前 Provider 字段答
        // "docker"，下面三行命令/模式黑名单却始终来自 Local 配置 —— 于是管理端展示的
        // 是一份根本没在执行的规则，看上去还完全正常。
        var (deniedCommands, deniedPatterns) = _provider.Name switch
        {
            "docker" => (opts.Docker.DeniedCommands, opts.Docker.DeniedPatterns),
            _ => (opts.Local.DeniedCommands, opts.Local.DeniedPatterns)
        };

        var dto = new SandboxStatusDto
        {
            Enabled = opts.Enabled,
            Provider = _provider.Name,
            DataRoot = opts.DataRoot,
            DeniedCommands = deniedCommands.AsReadOnly(),
            DeniedPatterns = deniedPatterns.AsReadOnly(),
            // 环境变量黑名单只有本地 provider 有（容器里根本不继承宿主环境），
            // 其它 provider 下如实返回空表，而不是端出一份不适用的清单。
            EnvironmentBlacklist = _provider.Name == "local"
                ? opts.Local.EnvironmentBlacklist.AsReadOnly()
                : Array.Empty<string>()
        };

        return ApiResult<SandboxStatusDto>.Ok(dto);
    }
}
