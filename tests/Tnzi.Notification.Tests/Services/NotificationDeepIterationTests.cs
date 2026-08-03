using MockQueryable;
using Tnzi.Notification.Metadata;
using Message = Tnzi.Notification.Entities.Message;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// Notification 模块 E1 深度迭代测试
/// 覆盖: BatchCancelAsync, BatchDeleteAsync, QueryAsync 新过滤器,
///       GetScheduledAsync, GetStatisticsTrendAsync
/// </summary>
public class NotificationDeepIterationTests
{
    #region Test Infrastructure

    private readonly Mock<IRepository<Message, Guid>> _repositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly NotificationService _notificationService;
    private readonly NotificationQueryService _queryService;

    public NotificationDeepIterationTests()
    {
        // 初始化 Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _repositoryMock = new Mock<IRepository<Message, Guid>>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        // 设置 IServiceProvider mock (ApplicationService 需要 ILoggerFactory)
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        // NotificationService 需要多个依赖
        var emailSenderMock = new Mock<IEmailSender>();
        var smsSenderMock = new Mock<ISmsSender>();
        var pushSenderMock = new Mock<IPushSender>();
        var optionsMock = new Mock<IOptionsMonitor<NotificationOptions>>();
        optionsMock.Setup(x => x.CurrentValue).Returns(new NotificationOptions
        {
            MaxConcurrency = 5,
            SmsMaxContentLength = 140,
            Queue = new QueueOptions { Enabled = false },
            Retry = new RetryOptions { RetryDelaySeconds = 1 }
        });

        _notificationService = new NotificationService(
            _repositoryMock.Object,
            emailSenderMock.Object,
            smsSenderMock.Object,
            pushSenderMock.Object,
            _unitOfWorkMock.Object,
            optionsMock.Object,
            serviceProviderMock.Object);

        _queryService = new NotificationQueryService(
            _repositoryMock.Object,
            serviceProviderMock.Object);
    }

    /// <summary>
    /// 设置 repository AsQueryable mock，支持 EF Core 异步 LINQ
    /// </summary>
    private void SetupQueryable(List<Message> messages)
    {
        var mockQueryable = messages.BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
    }

    /// <summary>
    /// 创建测试用 Message 实体
    /// </summary>
    private static Message CreateMessage(
        NotificationStatus status,
        NotificationType type = NotificationType.Email,
        string category = "General",
        NotificationPriority priority = NotificationPriority.Normal,
        Guid? senderId = null,
        DateTime? creationTime = null,
        DateTime? scheduledTime = null,
        string subject = "Test Subject")
    {
        return new Message
        {
            Id = Guid.NewGuid(),
            Type = type,
            Subject = subject,
            Content = "Test Content",
            Status = status,
            Priority = priority,
            Category = category,
            SenderId = senderId,
            CreationTime = creationTime ?? DateTime.UtcNow,
            ScheduledTime = scheduledTime,
            TotalRecipientCount = 1,
            SuccessCount = status == NotificationStatus.Sent ? 1 : 0,
            FailureCount = status == NotificationStatus.Failed ? 1 : 0
        };
    }

    #endregion

    #region E1-1: BatchCancelAsync Tests

    [Fact]
    public async Task BatchCancelAsync_Should_Cancel_Only_Pending_And_Scheduled()
    {
        // Arrange
        var pendingMsg = CreateMessage(NotificationStatus.Pending);
        var scheduledMsg = CreateMessage(NotificationStatus.Scheduled);
        var sentMsg = CreateMessage(NotificationStatus.Sent);
        var failedMsg = CreateMessage(NotificationStatus.Failed);
        var cancelledMsg = CreateMessage(NotificationStatus.Cancelled);

        var allMessages = new List<Message> { pendingMsg, scheduledMsg, sentMsg, failedMsg, cancelledMsg };
        var ids = allMessages.Select(m => m.Id).ToList();
        SetupQueryable(allMessages);

        // Act
        var result = await _notificationService.BatchCancelAsync(ids);

        // Assert - 只有 Pending 和 Scheduled 应该被取消
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
        _repositoryMock.Verify(r => r.UpdateManyAsync(
            It.Is<List<Message>>(list =>
                list.Count == 2 &&
                list.All(m => m.Status == NotificationStatus.Cancelled)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchCancelAsync_Should_Return_Zero_When_No_Cancellable_Found()
    {
        // Arrange - 全部是已发送/已失败状态
        var sentMsg = CreateMessage(NotificationStatus.Sent);
        var failedMsg = CreateMessage(NotificationStatus.Failed);
        var allMessages = new List<Message> { sentMsg, failedMsg };
        var ids = allMessages.Select(m => m.Id).ToList();
        SetupQueryable(allMessages);

        // Act
        var result = await _notificationService.BatchCancelAsync(ids);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
        _repositoryMock.Verify(r => r.UpdateManyAsync(It.IsAny<List<Message>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BatchCancelAsync_Should_Throw_When_Ids_Empty()
    {
        // Arrange
        var emptyIds = new List<Guid>();

        // Act & Assert - Check.NotNullOrEmpty 抛 ArgumentException
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _notificationService.BatchCancelAsync(emptyIds));
    }

    [Fact]
    public async Task BatchCancelAsync_Should_Throw_When_Ids_Null()
    {
        // Act & Assert - Check.NotNullOrEmpty 抛 ArgumentNullException
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await _notificationService.BatchCancelAsync(null!));
    }

    [Fact]
    public async Task BatchCancelAsync_Should_Save_Changes_After_Update()
    {
        // Arrange
        var pendingMsg = CreateMessage(NotificationStatus.Pending);
        SetupQueryable(new List<Message> { pendingMsg });

        // Act
        await _notificationService.BatchCancelAsync(new List<Guid> { pendingMsg.Id });

        // Assert
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchCancelAsync_Should_Only_Cancel_Matching_Ids()
    {
        // Arrange - 只请求部分 ID
        var pending1 = CreateMessage(NotificationStatus.Pending);
        var pending2 = CreateMessage(NotificationStatus.Pending);
        var pending3 = CreateMessage(NotificationStatus.Pending);
        SetupQueryable(new List<Message> { pending1, pending2, pending3 });

        // 只取消 pending1 和 pending2
        var ids = new List<Guid> { pending1.Id, pending2.Id };

        // Act
        var result = await _notificationService.BatchCancelAsync(ids);

        // Assert - 只取消请求的 2 个
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
    }

    #endregion

    #region E1-1: BatchDeleteAsync Tests

    [Fact]
    public async Task BatchDeleteAsync_Should_Delete_Found_Notifications()
    {
        // Arrange
        var msg1 = CreateMessage(NotificationStatus.Sent);
        var msg2 = CreateMessage(NotificationStatus.Failed);
        SetupQueryable(new List<Message> { msg1, msg2 });

        var ids = new List<Guid> { msg1.Id, msg2.Id };

        // Act
        var result = await _queryService.BatchDeleteAsync(ids);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
        _repositoryMock.Verify(r => r.DeleteManyAsync(
            It.Is<List<Message>>(list => list.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchDeleteAsync_Should_Return_Zero_When_No_Notifications_Found()
    {
        // Arrange - 没有匹配的 ID
        SetupQueryable(new List<Message>());
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        // Act
        var result = await _queryService.BatchDeleteAsync(ids);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
        _repositoryMock.Verify(r => r.DeleteManyAsync(It.IsAny<List<Message>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BatchDeleteAsync_Should_Throw_When_Ids_Empty()
    {
        // Act & Assert
        await Should.ThrowAsync<ArgumentException>(async () =>
            await _queryService.BatchDeleteAsync(new List<Guid>()));
    }

    [Fact]
    public async Task BatchDeleteAsync_Should_Throw_When_Ids_Null()
    {
        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await _queryService.BatchDeleteAsync(null!));
    }

    [Fact]
    public async Task BatchDeleteAsync_Should_Only_Delete_Matching_Ids()
    {
        // Arrange - msg3 的 ID 不在请求中
        var msg1 = CreateMessage(NotificationStatus.Sent);
        var msg2 = CreateMessage(NotificationStatus.Pending);
        var msg3 = CreateMessage(NotificationStatus.Failed);
        SetupQueryable(new List<Message> { msg1, msg2, msg3 });

        var ids = new List<Guid> { msg1.Id, msg2.Id };

        // Act
        var result = await _queryService.BatchDeleteAsync(ids);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
    }

    #endregion

    #region E1-2: QueryAsync with New Filters Tests

    [Fact]
    public async Task QueryAsync_Should_Filter_By_Category()
    {
        // Arrange
        var marketing = CreateMessage(NotificationStatus.Sent, category: "Marketing");
        var system = CreateMessage(NotificationStatus.Sent, category: "System");
        var general = CreateMessage(NotificationStatus.Sent, category: "General");
        SetupQueryable(new List<Message> { marketing, system, general });

        var request = new QueryNotificationRequest { Category = "Marketing", PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.QueryAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task QueryAsync_Should_Filter_By_Priority()
    {
        // Arrange
        var highPriority = CreateMessage(NotificationStatus.Sent, priority: NotificationPriority.High);
        var normalPriority = CreateMessage(NotificationStatus.Sent, priority: NotificationPriority.Normal);
        var lowPriority = CreateMessage(NotificationStatus.Sent, priority: NotificationPriority.Low);
        SetupQueryable(new List<Message> { highPriority, normalPriority, lowPriority });

        var request = new QueryNotificationRequest { Priority = NotificationPriority.High, PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.QueryAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task QueryAsync_Should_Filter_By_SenderId()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var otherSenderId = Guid.NewGuid();
        var msg1 = CreateMessage(NotificationStatus.Sent, senderId: senderId);
        var msg2 = CreateMessage(NotificationStatus.Sent, senderId: senderId);
        var msg3 = CreateMessage(NotificationStatus.Sent, senderId: otherSenderId);
        SetupQueryable(new List<Message> { msg1, msg2, msg3 });

        var request = new QueryNotificationRequest { SenderId = senderId, PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.QueryAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task QueryAsync_Should_Apply_Combined_Filters()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var match = CreateMessage(NotificationStatus.Sent, category: "Marketing", priority: NotificationPriority.High, senderId: senderId);
        var wrongCategory = CreateMessage(NotificationStatus.Sent, category: "System", priority: NotificationPriority.High, senderId: senderId);
        var wrongPriority = CreateMessage(NotificationStatus.Sent, category: "Marketing", priority: NotificationPriority.Low, senderId: senderId);
        var wrongSender = CreateMessage(NotificationStatus.Sent, category: "Marketing", priority: NotificationPriority.High, senderId: Guid.NewGuid());
        SetupQueryable(new List<Message> { match, wrongCategory, wrongPriority, wrongSender });

        var request = new QueryNotificationRequest
        {
            Category = "Marketing",
            Priority = NotificationPriority.High,
            SenderId = senderId,
            PageSize = 10,
            PageIndex = 1
        };

        // Act
        var result = await _queryService.QueryAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task QueryAsync_Should_Return_All_When_No_Filters()
    {
        // Arrange
        var msg1 = CreateMessage(NotificationStatus.Sent, category: "Marketing");
        var msg2 = CreateMessage(NotificationStatus.Pending, category: "System");
        var msg3 = CreateMessage(NotificationStatus.Failed, category: "General");
        SetupQueryable(new List<Message> { msg1, msg2, msg3 });

        var request = new QueryNotificationRequest { PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.QueryAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(3);
    }

    #endregion

    #region E1-2: GetScheduledAsync Tests

    [Fact]
    public async Task GetScheduledAsync_Should_Return_Only_Scheduled_Status()
    {
        // Arrange
        var scheduled1 = CreateMessage(NotificationStatus.Scheduled, scheduledTime: DateTime.UtcNow.AddHours(1));
        var scheduled2 = CreateMessage(NotificationStatus.Scheduled, scheduledTime: DateTime.UtcNow.AddHours(2));
        var pending = CreateMessage(NotificationStatus.Pending);
        var sent = CreateMessage(NotificationStatus.Sent);
        SetupQueryable(new List<Message> { scheduled1, scheduled2, pending, sent });

        var request = new QueryNotificationRequest { PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.GetScheduledAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetScheduledAsync_Should_Order_By_ScheduledTime_Ascending()
    {
        // Arrange
        var laterScheduled = CreateMessage(NotificationStatus.Scheduled,
            scheduledTime: DateTime.UtcNow.AddHours(3), subject: "Later");
        var earlierScheduled = CreateMessage(NotificationStatus.Scheduled,
            scheduledTime: DateTime.UtcNow.AddHours(1), subject: "Earlier");
        var midScheduled = CreateMessage(NotificationStatus.Scheduled,
            scheduledTime: DateTime.UtcNow.AddHours(2), subject: "Mid");
        SetupQueryable(new List<Message> { laterScheduled, earlierScheduled, midScheduled });

        var request = new QueryNotificationRequest { PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.GetScheduledAsync(request);

        // Assert - 检查按 ScheduledTime 升序
        result.Succeeded.ShouldBeTrue();
        var items = result.Data!.Items.ToList();
        items.Count.ShouldBe(3);
        items[0].ScheduledTime!.Value.ShouldBeLessThan(items[1].ScheduledTime!.Value);
        items[1].ScheduledTime!.Value.ShouldBeLessThan(items[2].ScheduledTime!.Value);
    }

    [Fact]
    public async Task GetScheduledAsync_Should_Filter_By_Type()
    {
        // Arrange
        var emailScheduled = CreateMessage(NotificationStatus.Scheduled, type: NotificationType.Email,
            scheduledTime: DateTime.UtcNow.AddHours(1));
        var smsScheduled = CreateMessage(NotificationStatus.Scheduled, type: NotificationType.Sms,
            scheduledTime: DateTime.UtcNow.AddHours(1));
        SetupQueryable(new List<Message> { emailScheduled, smsScheduled });

        var request = new QueryNotificationRequest { Type = NotificationType.Email, PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.GetScheduledAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetScheduledAsync_Should_Filter_By_Category()
    {
        // Arrange
        var marketing = CreateMessage(NotificationStatus.Scheduled, category: "Marketing",
            scheduledTime: DateTime.UtcNow.AddHours(1));
        var system = CreateMessage(NotificationStatus.Scheduled, category: "System",
            scheduledTime: DateTime.UtcNow.AddHours(1));
        SetupQueryable(new List<Message> { marketing, system });

        var request = new QueryNotificationRequest { Category = "Marketing", PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.GetScheduledAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetScheduledAsync_Should_Filter_By_Priority()
    {
        // Arrange
        var high = CreateMessage(NotificationStatus.Scheduled, priority: NotificationPriority.High,
            scheduledTime: DateTime.UtcNow.AddHours(1));
        var normal = CreateMessage(NotificationStatus.Scheduled, priority: NotificationPriority.Normal,
            scheduledTime: DateTime.UtcNow.AddHours(1));
        SetupQueryable(new List<Message> { high, normal });

        var request = new QueryNotificationRequest { Priority = NotificationPriority.High, PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.GetScheduledAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetScheduledAsync_Should_Filter_By_ScheduledTime_Range()
    {
        // Arrange - StartTime/EndTime 过滤 ScheduledTime 而不是 CreationTime
        var baseTime = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var inRange = CreateMessage(NotificationStatus.Scheduled,
            scheduledTime: baseTime.AddHours(12),
            creationTime: baseTime.AddDays(-1)); // creationTime 在范围外
        var beforeRange = CreateMessage(NotificationStatus.Scheduled,
            scheduledTime: baseTime.AddHours(-1),
            creationTime: baseTime); // scheduledTime 在范围前
        var afterRange = CreateMessage(NotificationStatus.Scheduled,
            scheduledTime: baseTime.AddDays(2),
            creationTime: baseTime); // scheduledTime 在范围后
        SetupQueryable(new List<Message> { inRange, beforeRange, afterRange });

        var request = new QueryNotificationRequest
        {
            StartTime = baseTime,
            EndTime = baseTime.AddDays(1),
            PageSize = 10,
            PageIndex = 1
        };

        // Act
        var result = await _queryService.GetScheduledAsync(request);

        // Assert - 只返回 scheduledTime 在范围内的
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetScheduledAsync_Should_Return_Empty_When_No_Scheduled()
    {
        // Arrange
        var pending = CreateMessage(NotificationStatus.Pending);
        var sent = CreateMessage(NotificationStatus.Sent);
        SetupQueryable(new List<Message> { pending, sent });

        var request = new QueryNotificationRequest { PageSize = 10, PageIndex = 1 };

        // Act
        var result = await _queryService.GetScheduledAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalCount.ShouldBe(0);
    }

    #endregion

    #region E1-3: GetStatisticsTrendAsync Tests

    [Fact]
    public async Task GetStatisticsTrendAsync_Daily_Should_Generate_Correct_Labels()
    {
        // Arrange
        var startDate = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

        var msg1 = CreateMessage(NotificationStatus.Sent, creationTime: startDate.AddHours(6));
        var msg2 = CreateMessage(NotificationStatus.Failed, creationTime: startDate.AddDays(1).AddHours(3));
        SetupQueryable(new List<Message> { msg1, msg2 });

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Daily, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var trend = result.Data!;
        trend.Interval.ShouldBe(TrendInterval.Daily);
        trend.DataPoints.Count.ShouldBe(3); // 2/25, 2/26, 2/27

        trend.DataPoints[0].Label.ShouldBe("2026-02-25");
        trend.DataPoints[1].Label.ShouldBe("2026-02-26");
        trend.DataPoints[2].Label.ShouldBe("2026-02-27");
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_Weekly_Should_Generate_Correct_Labels()
    {
        // Arrange - 跨 2 周
        var startDate = new DateTime(2026, 2, 16, 0, 0, 0, DateTimeKind.Utc); // 周一
        var endDate = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);    // 2周后

        SetupQueryable(new List<Message>());

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Weekly, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var trend = result.Data!;
        trend.Interval.ShouldBe(TrendInterval.Weekly);
        trend.DataPoints.Count.ShouldBe(2); // 2 weeks
        // 周标签格式: yyyy-Www
        trend.DataPoints[0].Label.ShouldStartWith("2026-W");
        trend.DataPoints[1].Label.ShouldStartWith("2026-W");
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_Monthly_Should_Generate_Correct_Labels()
    {
        // Arrange - 跨 3 个月
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        SetupQueryable(new List<Message>());

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Monthly, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var trend = result.Data!;
        trend.Interval.ShouldBe(TrendInterval.Monthly);
        trend.DataPoints.Count.ShouldBe(3); // Jan, Feb, Mar
        trend.DataPoints[0].Label.ShouldBe("2026-01");
        trend.DataPoints[1].Label.ShouldBe("2026-02");
        trend.DataPoints[2].Label.ShouldBe("2026-03");
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_Should_Return_Error_When_EndDate_Before_StartDate()
    {
        // Arrange
        var startDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Daily, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_Should_Return_Error_When_EndDate_Equals_StartDate()
    {
        // Arrange
        var date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Daily, date, date);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_Should_Count_SentCount_And_FailedCount_Per_Period()
    {
        // Arrange
        var startDate = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 27, 0, 0, 0, DateTimeKind.Utc);

        // Day 1 (2/25): 2 sent, 1 failed, 1 pending
        var day1Sent1 = CreateMessage(NotificationStatus.Sent, creationTime: startDate.AddHours(1));
        var day1Sent2 = CreateMessage(NotificationStatus.Sent, creationTime: startDate.AddHours(5));
        var day1Failed = CreateMessage(NotificationStatus.Failed, creationTime: startDate.AddHours(8));
        var day1Pending = CreateMessage(NotificationStatus.Pending, creationTime: startDate.AddHours(10));

        // Day 2 (2/26): 1 sent, 2 failed, 1 partially sent
        var day2Sent = CreateMessage(NotificationStatus.Sent, creationTime: startDate.AddDays(1).AddHours(2));
        var day2Failed1 = CreateMessage(NotificationStatus.Failed, creationTime: startDate.AddDays(1).AddHours(4));
        var day2Failed2 = CreateMessage(NotificationStatus.Failed, creationTime: startDate.AddDays(1).AddHours(6));
        var day2Partial = CreateMessage(NotificationStatus.PartiallySent, creationTime: startDate.AddDays(1).AddHours(8));

        SetupQueryable(new List<Message>
        {
            day1Sent1, day1Sent2, day1Failed, day1Pending,
            day2Sent, day2Failed1, day2Failed2, day2Partial
        });

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Daily, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var trend = result.Data!;
        trend.DataPoints.Count.ShouldBe(2);

        // Day 1: total=4, sent=2 (Sent), failed=1
        var day1 = trend.DataPoints[0];
        day1.TotalCount.ShouldBe(4);
        day1.SentCount.ShouldBe(2);  // Sent + PartiallySent
        day1.FailedCount.ShouldBe(1);

        // Day 2: total=4, sent=2 (Sent + PartiallySent), failed=2
        var day2 = trend.DataPoints[1];
        day2.TotalCount.ShouldBe(4);
        day2.SentCount.ShouldBe(2);  // Sent + PartiallySent
        day2.FailedCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_Empty_Periods_Should_Have_Zero_Counts()
    {
        // Arrange - 数据只在第 2 天，第 1 天和第 3 天为空
        var startDate = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc);

        var day2Msg = CreateMessage(NotificationStatus.Sent, creationTime: startDate.AddDays(1).AddHours(5));
        SetupQueryable(new List<Message> { day2Msg });

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Daily, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var trend = result.Data!;
        trend.DataPoints.Count.ShouldBe(3);

        // Day 1 (empty)
        trend.DataPoints[0].TotalCount.ShouldBe(0);
        trend.DataPoints[0].SentCount.ShouldBe(0);
        trend.DataPoints[0].FailedCount.ShouldBe(0);

        // Day 2 (has data)
        trend.DataPoints[1].TotalCount.ShouldBe(1);
        trend.DataPoints[1].SentCount.ShouldBe(1);

        // Day 3 (empty)
        trend.DataPoints[2].TotalCount.ShouldBe(0);
        trend.DataPoints[2].SentCount.ShouldBe(0);
        trend.DataPoints[2].FailedCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_Should_Set_StartTime_On_DataPoints()
    {
        // Arrange
        var startDate = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 27, 0, 0, 0, DateTimeKind.Utc);
        SetupQueryable(new List<Message>());

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Daily, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var trend = result.Data!;
        trend.StartDate.ShouldBe(startDate);
        trend.EndDate.ShouldBe(endDate);
        trend.DataPoints[0].StartTime.ShouldBe(startDate);
        trend.DataPoints[1].StartTime.ShouldBe(startDate.AddDays(1));
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_Should_Only_Include_Notifications_In_Date_Range()
    {
        // Arrange - 有数据在范围外
        var startDate = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc);

        var inRange = CreateMessage(NotificationStatus.Sent, creationTime: startDate.AddHours(5));
        var beforeRange = CreateMessage(NotificationStatus.Sent, creationTime: startDate.AddDays(-1));
        var afterRange = CreateMessage(NotificationStatus.Sent, creationTime: endDate.AddDays(1));
        SetupQueryable(new List<Message> { inRange, beforeRange, afterRange });

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Daily, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var trend = result.Data!;
        trend.DataPoints.Count.ShouldBe(1);
        trend.DataPoints[0].TotalCount.ShouldBe(1); // 只有 inRange
    }

    [Fact]
    public async Task GetStatisticsTrendAsync_PartiallySent_Should_Count_As_Sent()
    {
        // Arrange - PartiallySent 应计入 SentCount
        var startDate = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc);

        var partial = CreateMessage(NotificationStatus.PartiallySent, creationTime: startDate.AddHours(3));
        SetupQueryable(new List<Message> { partial });

        // Act
        var result = await _queryService.GetStatisticsTrendAsync(TrendInterval.Daily, startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var point = result.Data!.DataPoints[0];
        point.TotalCount.ShouldBe(1);
        point.SentCount.ShouldBe(1); // PartiallySent 计入 SentCount
        point.FailedCount.ShouldBe(0);
    }

    #endregion
}
