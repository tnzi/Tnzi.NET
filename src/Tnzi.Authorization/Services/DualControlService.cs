// 文件级导入而非全局：本模块的 Options 命名空间里有一个 AuthorizationOptions，
// 与 Microsoft.AspNetCore.Authorization 的同名类型撞车，全局导入会让策略提供者编译不过。
using DualControlOptions = Tnzi.Authorization.Options.DualControlOptions;

namespace Tnzi.Authorization.Services;

/// <inheritdoc cref="IDualControlService"/>
public class DualControlService : ApplicationService, IDualControlService
{
    /// <summary>许可有效期下限（分钟）。低于它的许可在签发那一刻就已经没用了。</summary>
    private const int MinLifetimeMinutes = 1;

    /// <summary>许可有效期上限（分钟）= 30 天。放得再长，「两个人当场同意」就不再成立。</summary>
    private const int MaxLifetimeMinutes = 43200;

    private readonly IRepository<DualControlRequest, Guid> _repository;
    private readonly IOptionsMonitor<DualControlOptions> _options;
    private readonly IRepository<Tnzi.Identity.Entities.User, Guid>? _userRepository;

    /// <summary>
    /// 初始化一个 <see cref="DualControlService"/> 类型的新实例。
    /// </summary>
    /// <param name="serviceProvider">服务提供者。</param>
    /// <param name="repository">双人授权请求仓储。</param>
    /// <param name="options">双人授权配置。</param>
    /// <param name="userRepository">
    /// 用户仓储，仅用于把待办面上的 Guid 换成看得懂的名字。
    /// <b>可选注入</b>（跨模块依赖的既有写法，见 <c>SuperAdminBootstrapper</c>）：
    /// 未加载 Identity 时姓名列为空，其余功能不受影响。
    /// </param>
    public DualControlService(
        IServiceProvider serviceProvider,
        IRepository<DualControlRequest, Guid> repository,
        IOptionsMonitor<DualControlOptions> options,
        IRepository<Tnzi.Identity.Entities.User, Guid>? userRepository = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _options = Check.NotNull(options);
        _userRepository = userRepository;
    }

    /// <inheritdoc />
    public async Task<Result<DualControlRequestDto>> RequestAsync(
        DualControlRequestInput input,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(input.Operation))
        {
            return Fail<DualControlRequestDto>("Operation is required", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var requesterId = CurrentUser?.Id;
        if (requesterId is null || requesterId == Guid.Empty)
        {
            return Fail<DualControlRequestDto>("Not authenticated", 401, ErrorCodes.UNAUTHORIZED);
        }

        // 连点两下不该造出两张许可 —— 两张许可等于这个动作被批准了两次。
        var existing = await FindPendingAsync(input.Operation, input.TargetId, cancellationToken);
        if (existing != null)
        {
            return Ok(existing.MapTo<DualControlRequestDto>());
        }

        // 钳住配置值：`DefaultLifetimeMinutes` 配成 0 或负数会让每一张许可在签发那一刻就已过期，
        // 而症状是「批准了却用不了」，没有任何东西会报错。
        var configured = Math.Clamp(_options.CurrentValue.DefaultLifetimeMinutes, MinLifetimeMinutes, MaxLifetimeMinutes);
        var lifetime = input.Lifetime ?? TimeSpan.FromMinutes(configured);

        if (lifetime <= TimeSpan.Zero)
        {
            return Fail<DualControlRequestDto>("Lifetime must be positive", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var request = new DualControlRequest
        {
            Operation = input.Operation.Trim(),
            TargetId = input.TargetId,
            PayloadJson = input.PayloadJson,
            Description = input.Description,
            Status = DualControlStatus.Pending,
            RequesterId = requesterId.Value,
            ExpiresAt = DateTime.UtcNow.Add(lifetime)
        };

        await _repository.InsertAsync(request, cancellationToken: cancellationToken);

        // 送达策略（站内信 / 邮件 / 值班电话）与业务紧要程度有关，框架猜不出来，
        // 因此只发事件，接不接由应用决定。
        await PublishEventAsync(new DualControlRequestedEvent
        {
            RequestId = request.Id,
            Operation = request.Operation,
            TargetId = request.TargetId,
            RequesterId = request.RequesterId,
            ExpiresAt = request.ExpiresAt
        });

        return Ok(request.MapTo<DualControlRequestDto>());
    }

    /// <inheritdoc />
    public async Task<Result<DualControlRequestDto>> ApproveAsync(
        Guid requestId,
        string? comment = null,
        CancellationToken cancellationToken = default)
        => await DecideAsync(requestId, DualControlStatus.Approved, comment, cancellationToken);

    /// <inheritdoc />
    public async Task<Result<DualControlRequestDto>> RejectAsync(
        Guid requestId,
        string? reason = null,
        CancellationToken cancellationToken = default)
        => await DecideAsync(requestId, DualControlStatus.Rejected, reason, cancellationToken);

    /// <inheritdoc />
    public async Task<Result> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _repository.GetAsync(requestId, cancellationToken);
        if (request == null)
        {
            return Fail("Request not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (request.Status != DualControlStatus.Pending)
        {
            return Fail("Only a pending request can be cancelled", 409, ErrorCodes.BUSINESS_ERROR);
        }

        if (request.RequesterId != CurrentUser?.Id)
        {
            // 撤回只属于发起人。别人要让它不生效，走拒绝 —— 那会留下是谁拒的。
            return Fail("Only the requester can cancel this request", 403, ErrorCodes.FORBIDDEN);
        }

        request.Status = DualControlStatus.Cancelled;
        request.DecidedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(request, cancellationToken: cancellationToken);

        return Ok();
    }

    /// <inheritdoc />
    public async Task<Result<DualControlRequestDto>> ConsumeAsync(
        Guid requestId,
        string operation,
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNullOrWhiteSpace(operation);

        var request = await _repository.GetAsync(requestId, cancellationToken);
        if (request == null)
        {
            return Fail<DualControlRequestDto>("Request not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 动作对不上说明拿错了许可 —— 一张为「作废工资单」批的许可不该能用来「删除账户」。
        if (!string.Equals(request.Operation, operation.Trim(), StringComparison.Ordinal))
        {
            return NotUsable();
        }

        if (request.Status != DualControlStatus.Approved || request.IsConsumed)
        {
            return NotUsable();
        }

        if (request.ExpiresAt <= DateTime.UtcNow)
        {
            return Fail<DualControlRequestDto>("This approval has expired", 409, ErrorCodes.BUSINESS_ERROR);
        }

        // ★★★ 批准的是一份参数，不是一个操作名。
        // 少了这一比，批下来的「转 100 元」可以改成「转 100 万」再执行，
        // 而审计上看到的是一次完全合规的双人授权。
        if (!string.Equals(NormalizePayload(request.PayloadJson), NormalizePayload(payloadJson), StringComparison.Ordinal))
        {
            LogWarning(
                "Dual-control consume rejected for request {RequestId}: the payload differs from what was approved.",
                requestId);

            return Fail<DualControlRequestDto>(
                "The request payload differs from what was approved",
                409,
                ErrorCodes.BUSINESS_ERROR);
        }

        // ★★★ 抢占式消费，不是「读出来改一改再存回去」。
        // 两个并发的 Consume 都会读到 IsConsumed = false，都通过上面全部校验，
        // 然后各自把它写成 true —— 于是一张许可换到了两次执行，而「一次批准只能执行一次」
        // 这条不变量在最需要它的时刻（并发提交）正好失效。状态机保证不了唯一性，条件更新才行。
        var claimed = await _repository
            .Where(r => r.Id == requestId && !r.IsConsumed && r.Status == DualControlStatus.Approved)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.IsConsumed, true)
                    .SetProperty(r => r.ConsumedAt, DateTime.UtcNow),
                cancellationToken);

        if (claimed == 0)
        {
            // 走到这里说明校验通过之后、更新之前被别人抢先了。与「本来就用过」同一个回答。
            LogWarning("Dual-control permit {RequestId} was consumed concurrently by another caller.", requestId);
            return NotUsable();
        }

        request.IsConsumed = true;
        request.ConsumedAt = DateTime.UtcNow;

        LogInformation(
            "Dual-control permit {RequestId} consumed for {Operation} (requested by {RequesterId}, approved by {ApproverId}).",
            requestId,
            request.Operation,
            request.RequesterId,
            request.ApproverId);

        return Ok(request.MapTo<DualControlRequestDto>());
    }

    /// <inheritdoc />
    public async Task<Result<DualControlRequestDto>> GetAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var request = await _repository.GetAsync(requestId, cancellationToken);
        if (request == null)
        {
            return Fail<DualControlRequestDto>("Request not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var dto = request.MapTo<DualControlRequestDto>();
        await ResolveUserNamesAsync([dto], cancellationToken);

        return Ok(dto);
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<DualControlRequestDto>>> QueryAsync(
        DualControlQueryDto query,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _repository.Where(_ => true);

        if (!string.IsNullOrWhiteSpace(query.Operation))
        {
            var operation = query.Operation.Trim();
            queryable = queryable.Where(r => r.Operation == operation);
        }

        if (!string.IsNullOrWhiteSpace(query.TargetId))
        {
            queryable = queryable.Where(r => r.TargetId == query.TargetId);
        }

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(r => r.Status == query.Status.Value);
        }

        if (query.RequesterId.HasValue)
        {
            queryable = queryable.Where(r => r.RequesterId == query.RequesterId.Value);
        }

        if (query.UsableOnly == true)
        {
            // 与 DualControlRequestDto.IsUsable 同一套判据 —— 筛选与展示必须同源，
            // 否则列表里会出现点开发现用不了的条目。
            var now = DateTime.UtcNow;
            queryable = queryable.Where(r =>
                r.Status == DualControlStatus.Approved && !r.IsConsumed && r.ExpiresAt > now);
        }

        var totalCount = await queryable.CountAsync(cancellationToken);

        var items = await queryable
            .OrderByDescending(r => r.CreationTime)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken);

        var dtos = items.MapToList<DualControlRequestDto>();
        await ResolveUserNamesAsync(dtos, cancellationToken);

        var paged = new PagedList<DualControlRequestDto>(
            dtos,
            query.PageIndex,
            query.PageSize,
            totalCount);

        return Ok<IPagedList<DualControlRequestDto>>(paged);
    }

    /// <summary>
    /// 把一批 DTO 上的发起人 / 审批人 Guid 换成用户名。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 一次查询取回本页涉及的全部用户（去重后通常是个位数），而不是逐行查 ——
    /// 待办面按页读，逐行查会把一页 20 条变成最多 40 次往返。
    /// </para>
    /// <para>
    /// ★ Identity 未加载（或用户已被删除、被租户过滤器挡掉）时<b>静默留空</b>：
    /// 姓名是可读性而不是正确性，缺了它审批人依然做得出决定，
    /// 为一个显示字段让整个待办面 500 是不划算的交换。
    /// </para>
    /// </remarks>
    private async Task ResolveUserNamesAsync(
        IReadOnlyCollection<DualControlRequestDto> items,
        CancellationToken cancellationToken)
    {
        if (_userRepository == null || items.Count == 0)
        {
            return;
        }

        var ids = new HashSet<Guid>();
        foreach (var item in items)
        {
            ids.Add(item.RequesterId);
            if (item.ApproverId.HasValue)
            {
                ids.Add(item.ApproverId.Value);
            }
        }

        var names = await _userRepository
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken);

        foreach (var item in items)
        {
            if (names.TryGetValue(item.RequesterId, out var requesterName))
            {
                item.RequesterName = requesterName;
            }

            if (item.ApproverId.HasValue && names.TryGetValue(item.ApproverId.Value, out var approverName))
            {
                item.ApproverName = approverName;
            }
        }
    }

    /// <summary>
    /// 批准与拒绝共用的一段：找到请求、确认还在等、确认决定的人不是发起人。
    /// </summary>
    private async Task<Result<DualControlRequestDto>> DecideAsync(
        Guid requestId,
        DualControlStatus decision,
        string? comment,
        CancellationToken cancellationToken)
    {
        var approverId = CurrentUser?.Id;
        if (approverId is null || approverId == Guid.Empty)
        {
            return Fail<DualControlRequestDto>("Not authenticated", 401, ErrorCodes.UNAUTHORIZED);
        }

        var request = await _repository.GetAsync(requestId, cancellationToken);
        if (request == null)
        {
            return Fail<DualControlRequestDto>("Request not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (request.Status != DualControlStatus.Pending)
        {
            return Fail<DualControlRequestDto>("This request has already been decided", 409, ErrorCodes.BUSINESS_ERROR);
        }

        // ★★★ 四眼原则的全部内容就是这一句。拒绝也一样要拦：
        // 允许自己拒自己看似无害，但它让「决定人 ≠ 发起人」这条不变量出现例外，
        // 而例外一旦存在，下一个人就会问「那批准是不是也能有例外」。
        if (request.RequesterId == approverId.Value)
        {
            return Fail<DualControlRequestDto>(
                "The requester cannot decide their own request",
                403,
                ErrorCodes.FORBIDDEN);
        }

        var allowed = await CanDecideAsync(request.Operation);
        if (!allowed)
        {
            LogWarning(
                "User {ApproverId} is not allowed to decide dual-control requests for {Operation}.",
                approverId,
                request.Operation);

            return Fail<DualControlRequestDto>(
                "You are not allowed to decide this request",
                403,
                ErrorCodes.FORBIDDEN);
        }

        if (request.ExpiresAt <= DateTime.UtcNow)
        {
            return Fail<DualControlRequestDto>("This request has expired", 409, ErrorCodes.BUSINESS_ERROR);
        }

        request.Status = decision;
        request.ApproverId = approverId.Value;
        request.DecidedAt = DateTime.UtcNow;
        request.DecisionComment = comment;

        await _repository.UpdateAsync(request, cancellationToken: cancellationToken);

        await PublishEventAsync(new DualControlDecidedEvent
        {
            RequestId = request.Id,
            Operation = request.Operation,
            TargetId = request.TargetId,
            RequesterId = request.RequesterId,
            ApproverId = approverId.Value,
            Approved = decision == DualControlStatus.Approved
        });

        return Ok(request.MapTo<DualControlRequestDto>());
    }

    /// <summary>
    /// 当前用户能不能对这个动作做决定。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 按 <c>{Operation}{ApprovalPermissionSuffix}</c> 查权限（默认后缀 <c>.approve</c>），
    /// 于是「谁能批准作废工资单」与「谁能批准删账户」是两个可以分别授予的码，
    /// 而不是一个「能批准任何东西」的通行证。超管天然通过（<c>IPermissionChecker</c> 上游短路）。
    /// </para>
    /// <para>
    /// ★ <b>后缀配成空串即关闭本检查</b>，交由调用方在自己的批准端点上把关。
    /// 但默认是<b>开</b>的：这个模块的立场是 deny-by-default，而一个不设防的
    /// <c>ApproveAsync</c> 意味着任何登录用户都能批准任何请求 —— 只要那不是他自己发起的。
    /// </para>
    /// <para>
    /// ★ 权限检查器不可用（未加载 Authorization 的权限运行时）时<b>拒绝</b>而不是放行：
    /// 查不通的准入策略应当拒绝，与 <c>ILoginGuard</c> 的 fail-closed 同一判据。
    /// </para>
    /// </remarks>
    private async Task<bool> CanDecideAsync(string operation)
    {
        var suffix = _options.CurrentValue.ApprovalPermissionSuffix;
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return true;
        }

        var checker = PermissionChecker;
        if (checker == null)
        {
            LogError(
                "Dual-control approval requires IPermissionChecker, which is not registered. "
                + "Load the authorization runtime, or set Authorization:DualControl:ApprovalPermissionSuffix "
                + "to an empty string to check approval rights in your own endpoint.");

            return false;
        }

        return await checker.IsGrantedAsync(operation + suffix);
    }

    private async Task<DualControlRequest?> FindPendingAsync(
        string operation,
        string? targetId,
        CancellationToken cancellationToken)
    {
        var trimmed = operation.Trim();
        var now = DateTime.UtcNow;

        return await _repository.FirstOrDefaultAsync(
            r => r.Operation == trimmed
                 && r.TargetId == targetId
                 && r.Status == DualControlStatus.Pending
                 && r.ExpiresAt > now,
            cancellationToken);
    }

    /// <summary>
    /// 「没有参数」的两种写法（null 与空串）视为同一件事，其余逐字比对。
    /// </summary>
    /// <remarks>
    /// 刻意<b>不</b>做 JSON 语义归一化（不重排键、不忽略空白）：那需要解析这段内容，
    /// 而它是调用方给的任意字符串；更重要的是，宽松的比较正是这条守卫要防的东西。
    /// 调用方两次序列化同一个对象应当得到同一串字符 —— 做不到就说明参数本身不稳定，
    /// 那才是要修的地方。
    /// </remarks>
    private static string NormalizePayload(string? payload)
        => string.IsNullOrWhiteSpace(payload) ? string.Empty : payload;

    /// <summary>
    /// 不可用的几种情形共用一个回答：区分「没批准」「已用过」「动作对不上」
    /// 只会告诉调用方该往哪个方向试。
    /// </summary>
    private Result<DualControlRequestDto> NotUsable()
        => Fail<DualControlRequestDto>("No usable approval for this action", 403, ErrorCodes.FORBIDDEN);
}
