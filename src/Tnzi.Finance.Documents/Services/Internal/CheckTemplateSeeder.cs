using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Finance.Documents.Services.Internal;

/// <summary>
/// 内置支票模板（CPA-006 加拿大商用支票）的幂等播种
/// </summary>
/// <remarks>
/// 走 <see cref="IPostMigrationStartupTask"/>（在迁移之后、每次启动执行），空库首启即可用。
/// 语义是 <b>additive，永不覆盖</b>：已存在同名模板行则原样跳过，管理端对版式的编辑安全保留；
/// 若该行被删除，下次启动会重新播回出厂版式（同权限目录的「代码声明即契约」语义）。
/// 模板正文以嵌入资源随程序集分发（<c>Templates/check-cpa006-ca.cshtml</c>），
/// 不依赖发布布局的文件拷贝。
/// </remarks>
internal sealed class CheckTemplateSeeder : IPostMigrationStartupTask
{
    /// <summary>嵌入资源名后缀（资源全名带程序集根命名空间前缀，按后缀匹配以免受命名空间改动影响）。</summary>
    private const string ResourceSuffix = "Templates.check-cpa006-ca.cshtml";

    public async Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Check.NotNull(serviceProvider);

        // 根容器传入，作用域服务（仓储/DbContext）必须自建 scope。
        await using var scope = serviceProvider.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CheckTemplateSeeder>>();

        var repository = scope.ServiceProvider.GetService<IRepository<TemplateEntity, Guid>>();
        if (repository == null)
        {
            logger.LogWarning("Template repository is unavailable; skipping the built-in check template seed.");
            return;
        }

        var exists = await repository.AnyAsync(
            t => t.TemplateName == CheckTemplates.Cpa006Canada
                && t.Module == CheckTemplates.Module
                && t.Category == CheckTemplates.Category,
            cancellationToken);
        if (exists)
            return;

        var content = ReadEmbeddedTemplate();
        if (content == null)
        {
            logger.LogWarning("Embedded check template resource '{Resource}' was not found; skipping the seed.", ResourceSuffix);
            return;
        }

        var template = new TemplateEntity
        {
            TemplateName = CheckTemplates.Cpa006Canada,
            Module = CheckTemplates.Module,
            Category = CheckTemplates.Category,
            Type = TemplateType.Print,
            ContentTemplate = content,
            SubjectTemplate = string.Empty,
            IsActive = true,
            Description = CheckTemplates.Cpa006CanadaDescription
        };

        try
        {
            await repository.InsertAsync(template, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded the built-in check template '{TemplateName}'.", CheckTemplates.Cpa006Canada);
        }
        catch (DbUpdateException ex)
        {
            // 多实例同时首启时的竞态：另一实例已插入即视为完成。
            logger.LogWarning(ex, "Could not seed the built-in check template '{TemplateName}'; it may already exist.", CheckTemplates.Cpa006Canada);
        }
    }

    private static string? ReadEmbeddedTemplate()
    {
        var assembly = typeof(CheckTemplateSeeder).Assembly;
        var name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (name == null)
            return null;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
