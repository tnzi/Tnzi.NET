using Tnzi.Exceptions;

namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// <see cref="IRecordAccessAuditor.RecordOrDenyAsync"/>（默认接口方法）——
/// 把「登记不下来就拒绝这次读取」压缩成无法写错的一行。
/// </summary>
/// <remarks>
/// 动机：<c>RecordAsync</c> 返回 <c>Result</c> 而 <c>await ...RecordAsync(...)</c> 丢弃返回值
/// 是最顺手的写法——消费方一旦这么写，配额闸门静默失效（界面上写着有导出配额，实际谁也没被拦过）。
/// 这组测试直接测默认方法本身，用桩实现喂三种真实返回形态。
/// </remarks>
public class RecordAccessRecordOrDenyTests
{
    /// <summary>只实现抽象成员的最小桩：默认方法的逻辑就在接口上，桩只负责回放指定结果。</summary>
    private sealed class StubAuditor(Result recordResult) : IRecordAccessAuditor
    {
        public string? LastResourceType { get; private set; }
        public string? LastResourceId { get; private set; }
        public string? LastPurpose { get; private set; }

        public Task<Result> RecordAsync(string resourceType, string resourceId, string? purpose = null, CancellationToken cancellationToken = default)
        {
            LastResourceType = resourceType;
            LastResourceId = resourceId;
            LastPurpose = purpose;
            return Task.FromResult(recordResult);
        }

        public Task<Result> VerifyChainAsync(Guid? userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<IPagedList<RecordAccessDto>>> GetAccessesAsync(RecordAccessQueryDto query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<List<RecordAccessUserStatDto>>> GetUserStatisticsAsync(DateTime? startTime = null, DateTime? endTime = null, int topN = 20, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task RecordOrDeny_Succeeds_PassesArgumentsThroughAndReturns()
    {
        var stub = new StubAuditor(Result.Success());
        IRecordAccessAuditor auditor = stub;

        await auditor.RecordOrDenyAsync("Tip", "42", "case-review");

        stub.LastResourceType.ShouldBe("Tip");
        stub.LastResourceId.ShouldBe("42");
        stub.LastPurpose.ShouldBe("case-review");
    }

    [Fact]
    public async Task RecordOrDeny_QuotaExceeded_ThrowsRateLimit429()
    {
        // 与 RecordAccessAuditor 超配额时的真实返回形态一致（429 + 消息）
        IRecordAccessAuditor auditor = new StubAuditor(
            Result.Failure("Data access quota exceeded. Please contact your administrator.", 429));

        var ex = await Should.ThrowAsync<RateLimitException>(
            () => auditor.RecordOrDenyAsync("Tip", "42"));

        ex.HttpStatusCode.ShouldBe(429);
        ex.Message.ShouldContain("quota");
    }

    [Fact]
    public async Task RecordOrDeny_AuditWriteFailed_StillDenies()
    {
        // 链尾争用重试耗尽（500）：审计写不下来时放行读取等于这条读取没有痕迹，
        // 与「拒绝」这个选择矛盾——必须同样拒绝。
        IRecordAccessAuditor auditor = new StubAuditor(
            Result.Failure("Failed to record data access audit entry.", 500));

        var ex = await Should.ThrowAsync<BusinessException>(
            () => auditor.RecordOrDenyAsync("Tip", "42"));

        ex.HttpStatusCode.ShouldBe(500);
    }
}
