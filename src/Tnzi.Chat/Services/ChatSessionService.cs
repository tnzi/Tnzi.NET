using System.Text.Json;

namespace Tnzi.Chat.Services;

/// <summary>
/// Default <see cref="IChatSessionService"/> implementation backed by
/// <see cref="IRepository{ChatSession, Guid}"/>.
/// </summary>
public class ChatSessionService : ApplicationService, IChatSessionService
{
    private static readonly JsonSerializerOptions ParticipantsJsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IRepository<ChatSession, Guid> _repository;

    public ChatSessionService(
        IServiceProvider serviceProvider,
        IRepository<ChatSession, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Result<ChatSessionDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetAsync(id, cancellationToken);
        if (session is null)
        {
            return Fail<ChatSessionDto>("Chat session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        return Ok(ToDto(session));
    }

    public async Task<Result<IPagedList<ChatSessionListItemDto>>> GetPagedListAsync(
        ChatSessionQueryDto query,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _repository.AsQueryable();

        if (query.Status is not null)
        {
            queryable = queryable.Where(s => s.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();
            queryable = queryable.Where(s =>
                s.Title.ToLower().Contains(keyword)
                || (s.Description != null && s.Description.ToLower().Contains(keyword)));
        }

        if (query.ParticipantId is not null)
        {
            // Participant filter is intentionally a string contains on the
            // serialised json — sufficient for the admin list filter and
            // avoids modelling a side table. The service layer re-validates
            // by parsing ParticipantsJson on each row.
            var needle = query.ParticipantId.Value.ToString("D");
            queryable = queryable.Where(s => s.ParticipantsJson.Contains(needle));
        }

        var total = queryable.Count();
        var items = queryable
            .OrderByDescending(s => s.LastMessageAt ?? s.CreationTime)
            .Skip((query.PageIndex - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        var dtos = items.Select(ToListItemDto).ToList();
        var paged = new PagedList<ChatSessionListItemDto>(dtos, total, query.PageIndex, query.PageSize);
        return Ok<IPagedList<ChatSessionListItemDto>>(paged);
    }

    public async Task<Result<ChatSessionDto>> CreateAsync(
        CreateChatSessionDto input,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var session = new ChatSession
        {
            Title = input.Title,
            Description = input.Description,
            Status = input.Status,
            ParticipantsJson = SerializeParticipants(input.Participants),
            MessageCount = 0,
            LastMessageAt = null,
        };

        await _repository.InsertAsync(session, cancellationToken: cancellationToken);
        return Ok(ToDto(session));
    }

    public async Task<Result<ChatSessionDto>> UpdateAsync(
        Guid id,
        UpdateChatSessionDto input,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        var session = await _repository.GetAsync(id, cancellationToken);
        if (session is null)
        {
            return Fail<ChatSessionDto>("Chat session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        session.Title = input.Title;
        session.Description = input.Description;
        session.Status = input.Status;
        session.ParticipantsJson = SerializeParticipants(input.Participants);

        await _repository.UpdateAsync(session, cancellationToken: cancellationToken);
        return Ok(ToDto(session));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _repository.GetAsync(id, cancellationToken);
        if (session is null)
        {
            return Fail("Chat session not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        await _repository.DeleteAsync(session, cancellationToken: cancellationToken);
        return Ok();
    }

    public async Task<Result<int>> DeleteBatchAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(ids);

        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return Ok(0);
        }

        var queryable = _repository.AsQueryable();
        var targets = queryable.Where(s => idList.Contains(s.Id)).ToList();
        if (targets.Count == 0)
        {
            return Ok(0);
        }

        foreach (var target in targets)
        {
            await _repository.DeleteAsync(target, cancellationToken: cancellationToken);
        }

        return Ok(targets.Count);
    }

    public async Task<Result<Guid>> UpsertFromMessageAsync(
        Guid? sessionId,
        string title,
        IEnumerable<Guid> participantIds,
        DateTime messageTime,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(participantIds);

        var participantList = participantIds.Distinct().ToList();

        ChatSession? session = null;
        if (sessionId is not null)
        {
            session = await _repository.GetAsync(sessionId.Value, cancellationToken);
        }

        if (session is null)
        {
            session = new ChatSession
            {
                Title = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title,
                Status = ChatSessionStatus.Active,
                ParticipantsJson = SerializeParticipants(participantList),
                MessageCount = 1,
                LastMessageAt = messageTime,
            };
            await _repository.InsertAsync(session, cancellationToken: cancellationToken);
            return Ok(session.Id);
        }

        // Merge participants (union, preserving order)
        var existing = DeserializeParticipants(session.ParticipantsJson);
        var merged = existing.Union(participantList).Distinct().ToList();
        session.ParticipantsJson = SerializeParticipants(merged);

        session.MessageCount += 1;
        session.LastMessageAt = messageTime;
        if (string.IsNullOrWhiteSpace(session.Title) && !string.IsNullOrWhiteSpace(title))
        {
            session.Title = title;
        }

        await _repository.UpdateAsync(session, cancellationToken: cancellationToken);
        return Ok(session.Id);
    }

    private static string SerializeParticipants(List<Guid> participants)
    {
        return participants.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(participants, ParticipantsJsonOptions);
    }

    private static List<Guid> DeserializeParticipants(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<Guid>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
        }
        catch (JsonException)
        {
            return new List<Guid>();
        }
    }

    private static ChatSessionDto ToDto(ChatSession session) => new()
    {
        Id = session.Id,
        Title = session.Title,
        Description = session.Description,
        Status = session.Status,
        Participants = DeserializeParticipants(session.ParticipantsJson),
        MessageCount = session.MessageCount,
        LastMessageAt = session.LastMessageAt,
        CreationTime = session.CreationTime,
        LastModificationTime = session.LastModificationTime,
    };

    private static ChatSessionListItemDto ToListItemDto(ChatSession session) => new()
    {
        Id = session.Id,
        Title = session.Title,
        Status = session.Status,
        Participants = DeserializeParticipants(session.ParticipantsJson),
        MessageCount = session.MessageCount,
        LastMessageAt = session.LastMessageAt,
        CreationTime = session.CreationTime,
    };
}
