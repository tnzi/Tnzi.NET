namespace Tnzi.AI.Tests;

/// <summary>
/// <see cref="IUsageLimitService"/> 的替身：放行（窗口照开、结算照收）或在开始时拒绝。
/// </summary>
internal static class UsageLimitMocks
{
    /// <summary>每一轮都放行、什么都不拦的计量。</summary>
    public static Mock<IUsageLimitService> Allowing()
    {
        var limits = new Mock<IUsageLimitService>();
        limits.Setup(l => l.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid userId, CancellationToken _) =>
                Result<UsageLimitReservation>.Success(new UsageLimitReservation(userId, DateTime.UtcNow)));
        limits.Setup(l => l.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        limits.Setup(l => l.CheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UsageLimitDenialDto?)null);
        return limits;
    }

    /// <summary>一轮开始时的拒绝（真实服务的形状：429 + 错误码 + 窗口与重置时间作错误详情）。</summary>
    public static Result<UsageLimitReservation> Denial(UsageLimitWindow window = UsageLimitWindow.Session, DateTime? resetsAt = null)
        => Result<UsageLimitReservation>.Failure(
            "You have reached your 5-hour usage limit.", 429, ErrorCodes.UsageLimitReached,
            new UsageLimitDenialDto { Window = window, ResetsAt = resetsAt ?? new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc) });
}
