namespace Tnzi.Chat.Tests.Services;

/// <summary>
/// MessageService 深度迭代 E1 增强测试
/// AdminBatchDeleteAsync + GetStatisticsAsync + BatchDeleteReceivesAsync + BatchMarkAsReadAsync
/// </summary>
public class AdminBatchDeleteTests
{
    private readonly Mock<IRepository<Message, Guid>> _messageRepoMock;
    private readonly Mock<IRepository<MessageReceive, Guid>> _receiveRepoMock;
    private readonly Mock<IRepository<MessageRecipient, Guid>> _recipientRepoMock;
    private readonly Mock<IRepository<MessageReply, Guid>> _replyRepoMock;
    private readonly Mock<IRepository<MessageRole, Guid>> _roleRepoMock;
    private readonly Mock<IRepository<User, Guid>> _userRepoMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly MessageService _service;

    public AdminBatchDeleteTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _messageRepoMock = new Mock<IRepository<Message, Guid>>();
        _receiveRepoMock = new Mock<IRepository<MessageReceive, Guid>>();
        _recipientRepoMock = new Mock<IRepository<MessageRecipient, Guid>>();
        _replyRepoMock = new Mock<IRepository<MessageReply, Guid>>();
        _roleRepoMock = new Mock<IRepository<MessageRole, Guid>>();
        _userRepoMock = new Mock<IRepository<User, Guid>>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new MessageService(
            _messageRepoMock.Object,
            _receiveRepoMock.Object,
            _recipientRepoMock.Object,
            _replyRepoMock.Object,
            _roleRepoMock.Object,
            _userRepoMock.Object,
            _serviceProviderMock.Object);
    }

    /// <summary>
    /// 设置 Message 仓储的 IQueryable mock
    /// IRepository 继承 IQueryable, .Where() 是 LINQ 扩展方法，通过 IQueryable 接口成员生效
    /// </summary>
    private void SetupMessageQueryable(List<Message> messages)
    {
        var mockQueryable = messages.BuildMock();
        _messageRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        _messageRepoMock.As<IQueryable<Message>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _messageRepoMock.As<IQueryable<Message>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _messageRepoMock.As<IQueryable<Message>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _messageRepoMock.As<IQueryable<Message>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private void SetupReceiveQueryable(List<MessageReceive> receives)
    {
        var mockQueryable = receives.BuildMock();
        _receiveRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        _receiveRepoMock.As<IQueryable<MessageReceive>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _receiveRepoMock.As<IQueryable<MessageReceive>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _receiveRepoMock.As<IQueryable<MessageReceive>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _receiveRepoMock.As<IQueryable<MessageReceive>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private void SetupReplyQueryable(List<MessageReply> replies)
    {
        var mockQueryable = replies.BuildMock();
        _replyRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        _replyRepoMock.As<IQueryable<MessageReply>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _replyRepoMock.As<IQueryable<MessageReply>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _replyRepoMock.As<IQueryable<MessageReply>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _replyRepoMock.As<IQueryable<MessageReply>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private void SetupRecipientQueryable(List<MessageRecipient> recipients)
    {
        var mockQueryable = recipients.BuildMock();
        _recipientRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        _recipientRepoMock.As<IQueryable<MessageRecipient>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _recipientRepoMock.As<IQueryable<MessageRecipient>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _recipientRepoMock.As<IQueryable<MessageRecipient>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _recipientRepoMock.As<IQueryable<MessageRecipient>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private void SetupRoleQueryable(List<MessageRole> roles)
    {
        var mockQueryable = roles.BuildMock();
        _roleRepoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        _roleRepoMock.As<IQueryable<MessageRole>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _roleRepoMock.As<IQueryable<MessageRole>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _roleRepoMock.As<IQueryable<MessageRole>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _roleRepoMock.As<IQueryable<MessageRole>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private static Message CreateMessage(Guid? id = null, MessageType type = MessageType.Private, bool isSent = true, bool isImportant = false, Guid? senderId = null, DateTime? creationTime = null)
    {
        return new Message
        {
            Id = id ?? Guid.NewGuid(),
            Title = "Test Message",
            Content = "Test Content",
            MessageType = type,
            SenderId = senderId ?? Guid.NewGuid(),
            IsSent = isSent,
            IsImportant = isImportant,
            CreationTime = creationTime ?? DateTime.UtcNow
        };
    }

    private static MessageReceive CreateReceive(Guid messageId, Guid userId, DateTime? readTime = null)
    {
        return new MessageReceive
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            UserId = userId,
            ReadTime = readTime
        };
    }

    #region AdminBatchDeleteAsync

    [Fact]
    public async Task AdminBatchDeleteAsync_Should_Delete_Matching_Messages()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var messages = new List<Message> { CreateMessage(id1), CreateMessage(id2) };

        SetupMessageQueryable(messages);
        SetupReceiveQueryable(new List<MessageReceive>());
        SetupRecipientQueryable(new List<MessageRecipient>());
        SetupRoleQueryable(new List<MessageRole>());
        SetupReplyQueryable(new List<MessageReply>());
        _messageRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AdminBatchDeleteAsync(new List<Guid> { id1, id2 });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
        _messageRepoMock.Verify(r => r.DeleteManyAsync(It.Is<IEnumerable<Message>>(m => m.Count() == 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminBatchDeleteAsync_NoMatchingMessages_Should_Return_Zero()
    {
        // Arrange
        SetupMessageQueryable(new List<Message>());

        // Act
        var result = await _service.AdminBatchDeleteAsync(new List<Guid> { Guid.NewGuid() });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
        _messageRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminBatchDeleteAsync_PartialMatch_Should_Delete_Only_Found()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var messages = new List<Message> { CreateMessage(id1) };
        SetupMessageQueryable(messages);
        SetupReceiveQueryable(new List<MessageReceive>());
        SetupRecipientQueryable(new List<MessageRecipient>());
        SetupRoleQueryable(new List<MessageRole>());
        SetupReplyQueryable(new List<MessageReply>());
        _messageRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act: 请求删除 2 个，只找到 1 个
        var result = await _service.AdminBatchDeleteAsync(new List<Guid> { id1, Guid.NewGuid() });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(1);
    }

    [Fact]
    public void AdminBatchDeleteAsync_NullIds_Should_Throw_ArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(async () =>
            await _service.AdminBatchDeleteAsync(null!));
    }

    [Fact]
    public void AdminBatchDeleteAsync_EmptyIds_Should_Throw_ArgumentException()
    {
        Should.Throw<ArgumentException>(async () =>
            await _service.AdminBatchDeleteAsync(new List<Guid>()));
    }

    #endregion

    #region GetStatisticsAsync

    [Fact]
    public async Task GetStatisticsAsync_Should_Return_Correct_Counts()
    {
        // Arrange
        var sender1 = Guid.NewGuid();
        var sender2 = Guid.NewGuid();
        var messages = new List<Message>
        {
            CreateMessage(type: MessageType.Private, isSent: true, senderId: sender1),
            CreateMessage(type: MessageType.Private, isSent: true, senderId: sender1),
            CreateMessage(type: MessageType.Public, isSent: true, isImportant: true, senderId: sender2),
            CreateMessage(type: MessageType.Private, isSent: false, senderId: sender2), // 草稿
        };

        SetupMessageQueryable(messages);

        var replies = new List<MessageReply>
        {
            new() { Id = Guid.NewGuid(), Content = "Reply 1", CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Content = "Reply 2", CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Content = "Reply 3", CreationTime = DateTime.UtcNow },
        };
        SetupReplyQueryable(replies);

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        var stats = result.Data!;
        stats.TotalMessages.ShouldBe(4);
        stats.SentMessages.ShouldBe(3);
        stats.DraftMessages.ShouldBe(1);
        stats.PublicMessages.ShouldBe(1);
        stats.PrivateMessages.ShouldBe(3);
        stats.ActiveSenders.ShouldBe(2);
        stats.ImportantMessages.ShouldBe(1);
        stats.TotalReplies.ShouldBe(3);
    }

    [Fact]
    public async Task GetStatisticsAsync_WithDateFilter_Should_Filter_Messages()
    {
        // Arrange
        var startDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc);

        var messages = new List<Message>
        {
            CreateMessage(type: MessageType.Private, isSent: true, creationTime: new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)),
            CreateMessage(type: MessageType.Public, isSent: true, creationTime: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)), // 范围外
        };

        SetupMessageQueryable(messages);
        SetupReplyQueryable(new List<MessageReply>());

        // Act
        var result = await _service.GetStatisticsAsync(startDate, endDate);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalMessages.ShouldBe(1);
        result.Data.PrivateMessages.ShouldBe(1);
        result.Data.PublicMessages.ShouldBe(0);
    }

    [Fact]
    public async Task GetStatisticsAsync_EmptyData_Should_Return_Zeros()
    {
        // Arrange
        SetupMessageQueryable(new List<Message>());
        SetupReplyQueryable(new List<MessageReply>());

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        var stats = result.Data!;
        stats.TotalMessages.ShouldBe(0);
        stats.SentMessages.ShouldBe(0);
        stats.DraftMessages.ShouldBe(0);
        stats.TotalReplies.ShouldBe(0);
        stats.ActiveSenders.ShouldBe(0);
        stats.ImportantMessages.ShouldBe(0);
        stats.PublicMessages.ShouldBe(0);
        stats.PrivateMessages.ShouldBe(0);
    }

    [Fact]
    public async Task GetStatisticsAsync_StartDateOnly_Should_Filter_Messages()
    {
        // Arrange: 只指定 startDate，不指定 endDate
        var startDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);
        var messages = new List<Message>
        {
            CreateMessage(type: MessageType.Private, isSent: true, creationTime: new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
            CreateMessage(type: MessageType.Public, isSent: true, creationTime: new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc)),
            CreateMessage(type: MessageType.Private, isSent: true, creationTime: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
        };

        SetupMessageQueryable(messages);
        SetupReplyQueryable(new List<MessageReply>());

        // Act
        var result = await _service.GetStatisticsAsync(startDate: startDate);

        // Assert: 2/20 和 3/1 在 startDate 之后
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalMessages.ShouldBe(2);
    }

    [Fact]
    public async Task GetStatisticsAsync_EndDateOnly_Should_Filter_Messages()
    {
        // Arrange: 只指定 endDate
        var endDate = new DateTime(2026, 2, 18, 0, 0, 0, DateTimeKind.Utc);
        var messages = new List<Message>
        {
            CreateMessage(type: MessageType.Private, isSent: true, creationTime: new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
            CreateMessage(type: MessageType.Public, isSent: true, creationTime: new DateTime(2026, 2, 16, 0, 0, 0, DateTimeKind.Utc)),
            CreateMessage(type: MessageType.Private, isSent: true, creationTime: new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc)),
        };

        SetupMessageQueryable(messages);
        SetupReplyQueryable(new List<MessageReply>());

        // Act
        var result = await _service.GetStatisticsAsync(endDate: endDate);

        // Assert: 2/10 和 2/16 在 endDate 之前
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalMessages.ShouldBe(2);
    }

    [Fact]
    public async Task GetStatisticsAsync_Should_Filter_Replies_By_DateRange()
    {
        // Arrange
        var startDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc);

        SetupMessageQueryable(new List<Message>());

        var replies = new List<MessageReply>
        {
            new() { Id = Guid.NewGuid(), Content = "r1", CreationTime = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc) },  // 范围外
            new() { Id = Guid.NewGuid(), Content = "r2", CreationTime = new DateTime(2026, 2, 12, 0, 0, 0, DateTimeKind.Utc) }, // 范围内
            new() { Id = Guid.NewGuid(), Content = "r3", CreationTime = new DateTime(2026, 2, 18, 0, 0, 0, DateTimeKind.Utc) }, // 范围内
            new() { Id = Guid.NewGuid(), Content = "r4", CreationTime = new DateTime(2026, 2, 25, 0, 0, 0, DateTimeKind.Utc) }, // 范围外
        };
        SetupReplyQueryable(replies);

        // Act
        var result = await _service.GetStatisticsAsync(startDate: startDate, endDate: endDate);

        // Assert: 只有 2 条回复在范围内
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalReplies.ShouldBe(2);
    }

    [Fact]
    public async Task GetStatisticsAsync_ActiveSenders_Should_Be_Distinct()
    {
        // Arrange: 同一 sender 发多条消息，ActiveSenders 应只计 1
        var senderId = Guid.NewGuid();
        var messages = new List<Message>
        {
            CreateMessage(type: MessageType.Private, isSent: true, senderId: senderId),
            CreateMessage(type: MessageType.Public, isSent: true, senderId: senderId),
            CreateMessage(type: MessageType.Private, isSent: false, senderId: senderId),
        };

        SetupMessageQueryable(messages);
        SetupReplyQueryable(new List<MessageReply>());

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.ActiveSenders.ShouldBe(1);
        result.Data.TotalMessages.ShouldBe(3);
    }

    [Fact]
    public async Task GetStatisticsAsync_AllPublic_Should_Count_Correctly()
    {
        // Arrange: 全部是 Public 消息
        var messages = new List<Message>
        {
            CreateMessage(type: MessageType.Public, isSent: true, senderId: Guid.NewGuid()),
            CreateMessage(type: MessageType.Public, isSent: true, senderId: Guid.NewGuid()),
        };

        SetupMessageQueryable(messages);
        SetupReplyQueryable(new List<MessageReply>());

        // Act
        var result = await _service.GetStatisticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.PublicMessages.ShouldBe(2);
        result.Data.PrivateMessages.ShouldBe(0);
    }

    #endregion

    #region BatchDeleteReceivesAsync

    [Fact]
    public async Task BatchDeleteReceivesAsync_Should_Delete_User_Receives()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var msgId1 = Guid.NewGuid();
        var msgId2 = Guid.NewGuid();
        var receives = new List<MessageReceive>
        {
            CreateReceive(msgId1, userId),
            CreateReceive(msgId2, userId),
        };

        SetupReceiveQueryable(receives);
        _receiveRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.BatchDeleteReceivesAsync(userId, new List<Guid> { msgId1, msgId2 });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
    }

    [Fact]
    public async Task BatchDeleteReceivesAsync_NoReceives_Should_Return_Zero()
    {
        // Arrange
        SetupReceiveQueryable(new List<MessageReceive>());

        // Act
        var result = await _service.BatchDeleteReceivesAsync(Guid.NewGuid(), new List<Guid> { Guid.NewGuid() });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
        _receiveRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BatchDeleteReceivesAsync_Should_Not_Affect_Other_Users()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        var receives = new List<MessageReceive>
        {
            CreateReceive(msgId, userId),
            CreateReceive(msgId, otherUserId), // 其他用户的记录不应被删除
        };

        SetupReceiveQueryable(receives);
        _receiveRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.BatchDeleteReceivesAsync(userId, new List<Guid> { msgId });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(1); // 只删除该用户的记录
    }

    [Fact]
    public void BatchDeleteReceivesAsync_NullMessageIds_Should_Throw()
    {
        Should.Throw<ArgumentNullException>(async () =>
            await _service.BatchDeleteReceivesAsync(Guid.NewGuid(), null!));
    }

    [Fact]
    public void BatchDeleteReceivesAsync_EmptyMessageIds_Should_Throw()
    {
        Should.Throw<ArgumentException>(async () =>
            await _service.BatchDeleteReceivesAsync(Guid.NewGuid(), new List<Guid>()));
    }

    #endregion

    #region BatchMarkAsReadAsync

    [Fact]
    public async Task BatchMarkAsReadAsync_Should_Mark_Unread_Messages()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var msgId1 = Guid.NewGuid();
        var msgId2 = Guid.NewGuid();
        var receives = new List<MessageReceive>
        {
            CreateReceive(msgId1, userId), // 未读 (ReadTime = null)
            CreateReceive(msgId2, userId), // 未读
        };

        SetupReceiveQueryable(receives);
        _receiveRepoMock.Setup(r => r.UpdateManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.BatchMarkAsReadAsync(userId, new List<Guid> { msgId1, msgId2 });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
        _receiveRepoMock.Verify(r => r.UpdateManyAsync(
            It.Is<IEnumerable<MessageReceive>>(items => items.All(x => x.ReadTime != null)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchMarkAsReadAsync_AlreadyRead_Should_Return_Zero()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        var receives = new List<MessageReceive>
        {
            CreateReceive(msgId, userId, readTime: DateTime.UtcNow), // 已读
        };

        SetupReceiveQueryable(receives);

        // Act
        var result = await _service.BatchMarkAsReadAsync(userId, new List<Guid> { msgId });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
        _receiveRepoMock.Verify(r => r.UpdateManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BatchMarkAsReadAsync_MixedReadUnread_Should_Mark_Only_Unread()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var msgIdUnread = Guid.NewGuid();
        var msgIdRead = Guid.NewGuid();
        var receives = new List<MessageReceive>
        {
            CreateReceive(msgIdUnread, userId), // 未读
            CreateReceive(msgIdRead, userId, readTime: DateTime.UtcNow), // 已读
        };

        SetupReceiveQueryable(receives);
        _receiveRepoMock.Setup(r => r.UpdateManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.BatchMarkAsReadAsync(userId, new List<Guid> { msgIdUnread, msgIdRead });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(1); // 只标记了 1 条未读
    }

    [Fact]
    public async Task BatchMarkAsReadAsync_NoReceives_Should_Return_Zero()
    {
        // Arrange
        SetupReceiveQueryable(new List<MessageReceive>());

        // Act
        var result = await _service.BatchMarkAsReadAsync(Guid.NewGuid(), new List<Guid> { Guid.NewGuid() });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
    }

    [Fact]
    public async Task BatchMarkAsReadAsync_Should_Not_Affect_Other_Users()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var msgId = Guid.NewGuid();

        var otherReceive = CreateReceive(msgId, otherUserId); // 其他用户的未读消息
        SetupReceiveQueryable(new List<MessageReceive> { otherReceive });

        // Act
        var result = await _service.BatchMarkAsReadAsync(userId, new List<Guid> { msgId });

        // Assert: userId 没有匹配记录，otherUser 的记录不受影响
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(0);
        otherReceive.ReadTime.ShouldBeNull();
    }

    [Fact]
    public async Task BatchMarkAsReadAsync_Should_Set_ReadTime_Close_To_UtcNow()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        var receive = CreateReceive(msgId, userId);

        SetupReceiveQueryable(new List<MessageReceive> { receive });
        _receiveRepoMock.Setup(r => r.UpdateManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var before = DateTime.UtcNow;

        // Act
        var result = await _service.BatchMarkAsReadAsync(userId, new List<Guid> { msgId });

        var after = DateTime.UtcNow;

        // Assert: ReadTime 应在 before 和 after 之间
        result.Succeeded.ShouldBeTrue();
        receive.ReadTime.ShouldNotBeNull();
        receive.ReadTime!.Value.ShouldBeGreaterThanOrEqualTo(before);
        receive.ReadTime!.Value.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void BatchMarkAsReadAsync_NullMessageIds_Should_Throw()
    {
        Should.Throw<ArgumentNullException>(async () =>
            await _service.BatchMarkAsReadAsync(Guid.NewGuid(), null!));
    }

    [Fact]
    public void BatchMarkAsReadAsync_EmptyMessageIds_Should_Throw()
    {
        Should.Throw<ArgumentException>(async () =>
            await _service.BatchMarkAsReadAsync(Guid.NewGuid(), new List<Guid>()));
    }

    #endregion
}

/// <summary>
/// ChatStatisticsDto 契约测试
/// </summary>
public class ChatStatisticsDtoTests
{
    [Fact]
    public void Default_Values_Should_All_Be_Zero()
    {
        var dto = new ChatStatisticsDto();
        dto.TotalMessages.ShouldBe(0);
        dto.SentMessages.ShouldBe(0);
        dto.DraftMessages.ShouldBe(0);
        dto.PublicMessages.ShouldBe(0);
        dto.PrivateMessages.ShouldBe(0);
        dto.TotalReplies.ShouldBe(0);
        dto.ActiveSenders.ShouldBe(0);
        dto.ImportantMessages.ShouldBe(0);
    }

    [Fact]
    public void Should_Have_All_Required_Properties()
    {
        var dto = new ChatStatisticsDto
        {
            TotalMessages = 100,
            SentMessages = 80,
            DraftMessages = 20,
            PublicMessages = 60,
            PrivateMessages = 40,
            TotalReplies = 50,
            ActiveSenders = 15,
            ImportantMessages = 10
        };

        dto.TotalMessages.ShouldBe(100);
        dto.SentMessages.ShouldBe(80);
        dto.DraftMessages.ShouldBe(20);
        dto.PublicMessages.ShouldBe(60);
        dto.PrivateMessages.ShouldBe(40);
        dto.TotalReplies.ShouldBe(50);
        dto.ActiveSenders.ShouldBe(15);
        dto.ImportantMessages.ShouldBe(10);
    }
}

/// <summary>
/// MessageType 枚举契约测试
/// </summary>
public class MessageTypeEnumTests
{
    [Fact]
    public void Should_Have_Expected_Values()
    {
        MessageType.Public.ShouldBe((MessageType)1);
        MessageType.Private.ShouldBe((MessageType)2);
    }

    [Fact]
    public void Should_Have_Two_Values()
    {
        var values = Enum.GetValues<MessageType>();
        values.Length.ShouldBe(2);
    }
}

/// <summary>
/// 消息删除级联清理测试
/// 验证 DeleteAsync / AdminDeleteAsync / AdminBatchDeleteAsync 正确清理关联记录
/// </summary>
public class MessageDeleteCascadeTests
{
    private readonly Mock<IRepository<Message, Guid>> _messageRepoMock;
    private readonly Mock<IRepository<MessageReceive, Guid>> _receiveRepoMock;
    private readonly Mock<IRepository<MessageRecipient, Guid>> _recipientRepoMock;
    private readonly Mock<IRepository<MessageReply, Guid>> _replyRepoMock;
    private readonly Mock<IRepository<MessageRole, Guid>> _roleRepoMock;
    private readonly Mock<IRepository<User, Guid>> _userRepoMock;
    private readonly MessageService _service;

    public MessageDeleteCascadeTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _messageRepoMock = new Mock<IRepository<Message, Guid>>();
        _receiveRepoMock = new Mock<IRepository<MessageReceive, Guid>>();
        _recipientRepoMock = new Mock<IRepository<MessageRecipient, Guid>>();
        _replyRepoMock = new Mock<IRepository<MessageReply, Guid>>();
        _roleRepoMock = new Mock<IRepository<MessageRole, Guid>>();
        _userRepoMock = new Mock<IRepository<User, Guid>>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new MessageService(
            _messageRepoMock.Object,
            _receiveRepoMock.Object,
            _recipientRepoMock.Object,
            _replyRepoMock.Object,
            _roleRepoMock.Object,
            _userRepoMock.Object,
            serviceProviderMock.Object);
    }

    private void SetupQueryable<TEntity>(Mock<IRepository<TEntity, Guid>> repoMock, List<TEntity> data) where TEntity : class, IEntity<Guid>
    {
        var mockQueryable = data.BuildMock();
        repoMock.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(mockQueryable);
        repoMock.As<IQueryable<TEntity>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        repoMock.As<IQueryable<TEntity>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        repoMock.As<IQueryable<TEntity>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        repoMock.As<IQueryable<TEntity>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private void SetupDeleteMocks()
    {
        _messageRepoMock.Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _messageRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _receiveRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _recipientRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageRecipient>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _replyRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReply>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _roleRepoMock.Setup(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageRole>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    #region DeleteAsync — 级联清理

    [Fact]
    public async Task DeleteAsync_Should_Delete_Associated_Receives()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var message = new Message { Id = messageId, SenderId = senderId, Title = "T", Content = "C" };

        _messageRepoMock.Setup(r => r.GetAsync(messageId, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        SetupDeleteMocks();

        var receives = new List<MessageReceive>
        {
            new() { Id = Guid.NewGuid(), MessageId = messageId, UserId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), MessageId = messageId, UserId = Guid.NewGuid() }
        };
        SetupQueryable(_receiveRepoMock, receives);
        SetupQueryable(_recipientRepoMock, new List<MessageRecipient>());
        SetupQueryable(_roleRepoMock, new List<MessageRole>());
        SetupQueryable(_replyRepoMock, new List<MessageReply>());

        // Act
        var result = await _service.DeleteAsync(messageId, senderId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        _receiveRepoMock.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<MessageReceive>>(items => items.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Should_Delete_Associated_Recipients_And_Roles()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var message = new Message { Id = messageId, SenderId = senderId, Title = "T", Content = "C" };

        _messageRepoMock.Setup(r => r.GetAsync(messageId, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        SetupDeleteMocks();

        SetupQueryable(_receiveRepoMock, new List<MessageReceive>());
        SetupQueryable(_recipientRepoMock, new List<MessageRecipient>
        {
            new() { Id = Guid.NewGuid(), MessageId = messageId, UserId = Guid.NewGuid() }
        });
        SetupQueryable(_roleRepoMock, new List<MessageRole>
        {
            new() { Id = Guid.NewGuid(), MessageId = messageId, RoleId = Guid.NewGuid() }
        });
        SetupQueryable(_replyRepoMock, new List<MessageReply>());

        // Act
        var result = await _service.DeleteAsync(messageId, senderId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        _recipientRepoMock.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<MessageRecipient>>(items => items.Count() == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        _roleRepoMock.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<MessageRole>>(items => items.Count() == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_Should_Delete_Associated_Replies()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var message = new Message { Id = messageId, SenderId = senderId, Title = "T", Content = "C" };

        _messageRepoMock.Setup(r => r.GetAsync(messageId, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        SetupDeleteMocks();

        SetupQueryable(_receiveRepoMock, new List<MessageReceive>());
        SetupQueryable(_recipientRepoMock, new List<MessageRecipient>());
        SetupQueryable(_roleRepoMock, new List<MessageRole>());
        SetupQueryable(_replyRepoMock, new List<MessageReply>
        {
            new() { Id = Guid.NewGuid(), BelongMessageId = messageId, Content = "Reply 1", UserId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), BelongMessageId = messageId, Content = "Reply 2", UserId = Guid.NewGuid() }
        });

        // Act
        var result = await _service.DeleteAsync(messageId, senderId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        _replyRepoMock.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<MessageReply>>(items => items.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NoAssociatedRecords_Should_Not_Call_DeleteMany()
    {
        // Arrange
        var senderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var message = new Message { Id = messageId, SenderId = senderId, Title = "T", Content = "C" };

        _messageRepoMock.Setup(r => r.GetAsync(messageId, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        SetupDeleteMocks();

        SetupQueryable(_receiveRepoMock, new List<MessageReceive>());
        SetupQueryable(_recipientRepoMock, new List<MessageRecipient>());
        SetupQueryable(_roleRepoMock, new List<MessageRole>());
        SetupQueryable(_replyRepoMock, new List<MessageReply>());

        // Act
        var result = await _service.DeleteAsync(messageId, senderId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        _receiveRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()), Times.Never);
        _recipientRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageRecipient>>(), It.IsAny<CancellationToken>()), Times.Never);
        _roleRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageRole>>(), It.IsAny<CancellationToken>()), Times.Never);
        _replyRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReply>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region AdminDeleteAsync — 级联清理

    [Fact]
    public async Task AdminDeleteAsync_Should_Delete_All_Associated_Records()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        var message = new Message { Id = messageId, SenderId = Guid.NewGuid(), Title = "T", Content = "C" };

        _messageRepoMock.Setup(r => r.GetAsync(messageId, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        SetupDeleteMocks();

        SetupQueryable(_receiveRepoMock, new List<MessageReceive>
        {
            new() { Id = Guid.NewGuid(), MessageId = messageId, UserId = Guid.NewGuid() }
        });
        SetupQueryable(_recipientRepoMock, new List<MessageRecipient>
        {
            new() { Id = Guid.NewGuid(), MessageId = messageId, UserId = Guid.NewGuid() }
        });
        SetupQueryable(_roleRepoMock, new List<MessageRole>
        {
            new() { Id = Guid.NewGuid(), MessageId = messageId, RoleId = Guid.NewGuid() }
        });
        SetupQueryable(_replyRepoMock, new List<MessageReply>
        {
            new() { Id = Guid.NewGuid(), BelongMessageId = messageId, Content = "R", UserId = Guid.NewGuid() }
        });

        // Act
        var result = await _service.AdminDeleteAsync(messageId);

        // Assert
        result.Succeeded.ShouldBeTrue();
        _receiveRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()), Times.Once);
        _recipientRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageRecipient>>(), It.IsAny<CancellationToken>()), Times.Once);
        _roleRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageRole>>(), It.IsAny<CancellationToken>()), Times.Once);
        _replyRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReply>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AdminBatchDeleteAsync — 级联清理

    [Fact]
    public async Task AdminBatchDeleteAsync_Should_Delete_Associated_Records_For_All_Messages()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { Id = id1, SenderId = Guid.NewGuid(), Title = "T1", Content = "C1" },
            new() { Id = id2, SenderId = Guid.NewGuid(), Title = "T2", Content = "C2" }
        };

        SetupQueryable(_messageRepoMock, messages);
        SetupDeleteMocks();

        var receives = new List<MessageReceive>
        {
            new() { Id = Guid.NewGuid(), MessageId = id1, UserId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), MessageId = id2, UserId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), MessageId = id2, UserId = Guid.NewGuid() }
        };
        SetupQueryable(_receiveRepoMock, receives);
        SetupQueryable(_recipientRepoMock, new List<MessageRecipient>());
        SetupQueryable(_roleRepoMock, new List<MessageRole>());
        SetupQueryable(_replyRepoMock, new List<MessageReply>
        {
            new() { Id = Guid.NewGuid(), BelongMessageId = id1, Content = "R1", UserId = Guid.NewGuid() }
        });

        // Act
        var result = await _service.AdminBatchDeleteAsync(new List<Guid> { id1, id2 });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(2);
        _receiveRepoMock.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<MessageReceive>>(items => items.Count() == 3),
            It.IsAny<CancellationToken>()), Times.Once);
        _replyRepoMock.Verify(r => r.DeleteManyAsync(
            It.Is<IEnumerable<MessageReply>>(items => items.Count() == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminBatchDeleteAsync_NoAssociatedRecords_Should_Still_Delete_Messages()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { Id = id1, SenderId = Guid.NewGuid(), Title = "T1", Content = "C1" }
        };

        SetupQueryable(_messageRepoMock, messages);
        SetupDeleteMocks();

        SetupQueryable(_receiveRepoMock, new List<MessageReceive>());
        SetupQueryable(_recipientRepoMock, new List<MessageRecipient>());
        SetupQueryable(_roleRepoMock, new List<MessageRole>());
        SetupQueryable(_replyRepoMock, new List<MessageReply>());

        // Act
        var result = await _service.AdminBatchDeleteAsync(new List<Guid> { id1 });

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldBe(1);
        _messageRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<Message>>(), It.IsAny<CancellationToken>()), Times.Once);
        // No associated record deletes should be called
        _receiveRepoMock.Verify(r => r.DeleteManyAsync(It.IsAny<IEnumerable<MessageReceive>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
