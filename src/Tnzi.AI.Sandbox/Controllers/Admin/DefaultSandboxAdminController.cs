using Microsoft.AspNetCore.Mvc;
using Tnzi.AspNetCore.Models;
using Tnzi.AspNetCore.Mvc;

namespace Tnzi.AI.Sandbox.Controllers.Admin;

/// <summary>
/// Sandbox 管理控制器 — 查询沙箱配置和 Provider 状态
/// </summary>
[DefaultController]
[Route("admin/sandbox")]
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
        var dto = new SandboxStatusDto
        {
            Enabled = opts.Enabled,
            Provider = _provider.Name,
            DataRoot = opts.DataRoot,
            DeniedCommands = opts.Local.DeniedCommands.AsReadOnly(),
            DeniedPatterns = opts.Local.DeniedPatterns.AsReadOnly(),
            EnvironmentBlacklist = opts.Local.EnvironmentBlacklist.AsReadOnly()
        };

        return ApiResult<SandboxStatusDto>.Ok(dto);
    }
}
