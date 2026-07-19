namespace Tnzi.Audit.Services;

/// <summary>
/// 判定某个 HTTP 请求是否会产出操作审计记录（AuditOperation）：
/// 操作审计总开关开启、路径未被排除、端点未标 [AuditDisabled]。
/// <para>
/// AuditMiddleware 的请求门与 EntityAuditSaveChangesInterceptor 的采集门共用本判定，
/// 保证两侧永不漂移——实体条目唯一的出路是挂在请求级 AuditOperation 上，
/// 操作审计不落库的请求里做实体采集是纯浪费（如 /hubs 长连接下的写操作）。
/// </para>
/// </summary>
internal static class AuditOperationGate
{
    /// <summary>当前请求是否会产出 AuditOperation。options 由调用方传入统一快照。</summary>
    internal static bool ShouldAudit(HttpContext context, AuditOptions options)
    {
        if (!options.EnableOperationAudit)
        {
            return false;
        }

        if (IsExcludedPath(context.Request.Path, options.ExcludedPaths))
        {
            return false;
        }

        // 端点级 [AuditDisabled]（action 与 controller 级元数据均聚合在 Endpoint.Metadata）
        return context.GetEndpoint()?.Metadata.GetMetadata<AuditDisabledAttribute>() == null;
    }

    private static bool IsExcludedPath(PathString path, string[] excludedPaths)
    {
        foreach (var excluded in excludedPaths)
        {
            if (path.StartsWithSegments(excluded))
            {
                return true;
            }
        }

        return false;
    }
}
