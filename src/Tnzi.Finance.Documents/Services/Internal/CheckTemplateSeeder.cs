using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Finance.Documents.Services.Internal;

/// <summary>
/// 出厂支票版式（<see cref="BuiltInCheckTemplates.All"/>）的幂等播种
/// </summary>
/// <remarks>
/// 走 <see cref="IPostMigrationStartupTask"/>（在迁移之后、每次启动执行），空库首启即可用。
/// 逐份独立判断与写入 —— 新增一套出厂版式不会碰到任何既有行。
/// 模板正文以嵌入资源随程序集分发（<c>Templates/*.cshtml</c>），不依赖发布布局的文件拷贝。
/// <para>
/// ★ <b>一份的写入失败不连累其余</b>，且这句话的边界是明确的：<b>插入与刷新两条路径各自</b>
/// 兜住自己的写失败 —— 丢弃失败的实体（否则它留在变更跟踪器里，会被下一份的
/// <c>SaveChanges</c> 重放）、记一条<b>点名到具体版式</b>的日志、继续下一份。
/// 六套版式彼此无关，一份刷不动不该让其余五份也停在旧正文上 —— 那等于把本机制要解决的
/// 问题（出厂修复到不了已部署的库）按不同的原因重演一次。
/// </para>
/// <para>
/// ⚠️ 反过来，<b>不属于某一份的故障不在此列</b>（连不上库、查不动表）：那些整轮抛给
/// <see cref="IPostMigrationStartupTask"/> 的调用方处理。把「数据库不可达」摊成六条逐份错误，
/// 只会让一个系统性故障看起来像六个孤立问题。
/// </para>
/// <para>
/// ★★ 语义从「additive，永不覆盖」改为 <b>「刷新出厂版，永不覆盖用户编辑」</b>
/// （2026-09-02）。原语义的代价是：框架代码里修好的模板正文<b>到不了一个已经播种过的库</b> ——
/// 没有报错，只是纸印错，而每个消费应用都得自己想到要写一条数据迁移去删行重播。
/// 一天之内为此写两条迁移（存根附加行 + CPA-006 净空、样张票纸区分）之后，这条路走不下去了。
/// </para>
/// <para>
/// 判据见 <see cref="SeededTemplateRefresh"/>：播下去的正文连同指纹一起存进
/// <c>Template.Metadata</c>，此后「当前正文的指纹 == 存下的指纹」即说明自我们播下去之后
/// 没人动过。**用户改过的行一个字节都不会被碰**，那是 additive 当初存在的全部理由。
/// </para>
/// <para>
/// ★ 刷新只写<b>正文</b>：描述、启用状态、布局绑定都是消费应用可能特意调过的，
/// 而它们都不影响印在纸上的东西。
/// </para>
/// </remarks>
internal sealed class CheckTemplateSeeder : IPostMigrationStartupTask
{
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

        foreach (var builtIn in BuiltInCheckTemplates.All)
            await SeedOneAsync(repository, builtIn, logger, cancellationToken);
    }

    private static async Task SeedOneAsync(
        IRepository<TemplateEntity, Guid> repository,
        BuiltInCheckTemplate builtIn,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var content = ReadEmbeddedTemplate(builtIn.ResourceFile);
        if (content == null)
        {
            logger.LogWarning("Embedded check template resource '{Resource}' was not found; skipping the seed.", builtIn.ResourceFile);
            return;
        }

        var existing = await repository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(
                t => t.TemplateName == builtIn.Name
                    && t.Module == CheckTemplates.Module
                    && t.Category == CheckTemplates.Category,
                cancellationToken);

        if (existing == null)
        {
            await InsertAsync(repository, builtIn, content, logger, cancellationToken);
            return;
        }

        await RefreshAsync(repository, builtIn, existing, content, logger, cancellationToken);
    }

    private static async Task InsertAsync(
        IRepository<TemplateEntity, Guid> repository,
        BuiltInCheckTemplate builtIn,
        string content,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var template = new TemplateEntity
        {
            TemplateName = builtIn.Name,
            Module = CheckTemplates.Module,
            Category = CheckTemplates.Category,
            Type = TemplateType.Print,
            ContentTemplate = content,
            SubjectTemplate = string.Empty,
            IsActive = true,
            Description = builtIn.Description,
            // 指纹与正文同一次写入：日后要判断「这一行还是不是出厂版」，靠的就是它。
            Metadata = SeededTemplateRefresh.TryWriteFingerprint(null, SeededTemplateRefresh.Fingerprint(content))
        };

        try
        {
            await repository.InsertAsync(template, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded the built-in check template '{TemplateName}'.", builtIn.Name);
        }
        catch (DbUpdateException ex)
        {
            // 多实例同时首启时的竞态：另一实例已插入即视为完成。
            // ★ 必须丢弃这条失败的实体：它仍是 Added 留在变更跟踪器里，
            // 下一份版式的 SaveChanges 会把它一起重放并再次撞索引，一路传染到清单末尾。
            repository.Discard(template);
            logger.LogWarning(ex, "Could not seed the built-in check template '{TemplateName}'; it may already exist.", builtIn.Name);
        }
    }

    /// <summary>
    /// 已存在的行：仍是出厂版就刷成新正文，被人改过就一个字节都不碰。
    /// 写失败只影响这一份（丢弃实体 + 点名记 Error），调用方继续处理下一份。
    /// </summary>
    private static async Task RefreshAsync(
        IRepository<TemplateEntity, Guid> repository,
        BuiltInCheckTemplate builtIn,
        TemplateEntity existing,
        string content,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!SeededTemplateRefresh.IsFactoryOwned(existing.Metadata, existing.ContentTemplate, existing.LastModificationTime))
            return;

        var fingerprint = SeededTemplateRefresh.Fingerprint(content);
        if (SeededTemplateRefresh.ReadFingerprint(existing.Metadata) == fingerprint
            && string.Equals(existing.ContentTemplate, content, StringComparison.Ordinal))
        {
            // 已经是这一版，什么都不写 —— 每次启动都空写一遍会把
            // LastModificationTime 顶到当前时间，让「这行有没有被人动过」再也读不出来。
            return;
        }

        var metadata = SeededTemplateRefresh.TryWriteFingerprint(existing.Metadata, fingerprint);
        if (metadata == null)
        {
            // Metadata 不是 JSON 对象 —— 那是别人的数据结构，看不懂就不动，也不刷新。
            logger.LogWarning(
                "Check template '{TemplateName}' has metadata that is not a JSON object; leaving it untouched instead of refreshing it.",
                builtIn.Name);
            return;
        }

        existing.ContentTemplate = content;
        existing.Metadata = metadata;
        // 正文变了就是一个新修订号：管理端据此发现自己手上那份已经过时。
        existing.Version += 1;

        try
        {
            await repository.UpdateAsync(existing, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Refreshed the built-in check template '{TemplateName}' to the version shipped with the assembly.", builtIn.Name);
        }
        catch (DbUpdateException ex)
        {
            // ★ 与插入路径同一个理由，只是实体状态是 Modified 而不是 Added：
            // 失败的实体仍留在变更跟踪器里，下一份版式的 SaveChanges 会把它一起重放 ——
            // 要么再炸一次一路传染到清单末尾，要么更糟：**悄悄写进去**，而我们刚刚
            // 报告过它失败了。所以必须丢弃。
            repository.Discard(existing);

            // ★ Error 而不是 Warning：这一份版式停在旧正文上，除此之外没有任何症状 ——
            // 印出来的纸是错的，而接口、日志、管理端全都正常。这正是本机制存在的理由。
            // 插入路径那条是 Warning，因为「另一实例已经插过了」是良性竞态，结果是对的。
            logger.LogError(ex,
                "Could not refresh the built-in check template '{TemplateName}'; it stays on the body it already had. "
                + "The other built-in layouts are unaffected.", builtIn.Name);
        }
    }

    /// <summary>嵌入资源按后缀匹配（资源全名带程序集根命名空间前缀，避免受命名空间改动影响）。</summary>
    private static string? ReadEmbeddedTemplate(string resourceFile)
    {
        var assembly = typeof(CheckTemplateSeeder).Assembly;
        var suffix = "Templates." + resourceFile;
        var name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith(suffix, StringComparison.Ordinal));
        if (name == null)
            return null;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
