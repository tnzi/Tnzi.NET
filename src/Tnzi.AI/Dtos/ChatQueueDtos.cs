namespace Tnzi.AI.Dtos;

/// <summary>
/// A message queued in a thread while its turn runs (<c>GET chat/threads/{threadId}/queue</c>): handed to the running agent,
/// which takes it in between two of its steps, or, where the running turn cannot take messages, sent with the others as the
/// thread's next turn when that turn ends.
/// </summary>
public class QueuedMessageDto
{
    /// <summary>Queued message id.</summary>
    public Guid Id { get; set; }

    /// <summary>The thread it goes to.</summary>
    public Guid ThreadId { get; set; }

    /// <summary>The words it sends.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// <c>Queued</c> waits (to be handed to the running agent, or for the turn to end; it goes at once when none runs);
    /// <c>Delivering</c> is going out as the next turn right now; <c>Held</c> goes only when the user sends it now;
    /// <c>HandedOver</c> was written to the running agent, which has not taken it in yet; <c>Delivered</c> was taken in and
    /// is a user message of the thread (<see cref="ThreadMessageId"/>) until the turn that took it ends.
    /// </summary>
    public QueuedMessageState State { get; set; }

    /// <summary>It goes next, by itself, ahead of the others.</summary>
    public bool SendNow { get; set; }

    /// <summary>
    /// Why it is held: its turn was refused, its sending was interrupted, the agent did not take it in, or the turn it waited for
    /// was stopped (<see cref="FailureCode"/> <c>AI_QUEUED_TURN_STOPPED</c>). Null when it is not held.
    /// </summary>
    public string? Failure { get; set; }

    /// <summary>The error code of <see cref="Failure"/> (for example <c>AI_MODEL_NOT_ALLOWED</c>), for a localized message.</summary>
    public string? FailureCode { get; set; }

    /// <summary>The model chosen for it; null for the thread's default.</summary>
    public string? Model { get; set; }

    /// <summary>The reasoning effort chosen for it (a runtime-native value); null for none.</summary>
    public string? Effort { get; set; }

    /// <summary>The per-turn metadata it goes with (as on <c>ChatRequestDto.Metadata</c>); null for none.</summary>
    public Dictionary<string, JsonElement>? Metadata { get; set; }

    /// <summary>The run it waits for; null when none was running when it was queued.</summary>
    public Guid? AfterRunId { get; set; }

    /// <summary>When it was queued (UTC).</summary>
    public DateTime QueuedTime { get; set; }

    /// <summary>
    /// Its attachments (same rules as a chat message's); their content is at <c>GET threads/{threadId}/attachments/{id}</c>.
    /// </summary>
    public List<AttachmentDto> Attachments { get; set; } = [];

    /// <summary>The run it was handed to (<c>HandedOver</c> and <c>Delivered</c>); null otherwise.</summary>
    public Guid? HandedToRunId { get; set; }

    /// <summary>The thread's user message it became once the agent took it in (<c>Delivered</c>); null otherwise.</summary>
    public Guid? ThreadMessageId { get; set; }

    /// <summary>
    /// Sending it now stops the turn that is running: that turn cannot take a message while it works (its CLI or protocol
    /// cannot, its process runs where it may be reattached, or the message carries files the agent opens with its tools).
    /// False when nothing runs, or when it goes to the running agent without stopping anything.
    /// </summary>
    public bool SendNowStopsTurn { get; set; }
}

/// <summary>
/// A message to queue in a thread (<c>POST chat/threads/{threadId}/queue</c>): what the composer would have sent.
/// </summary>
public class QueueMessageRequestDto
{
    /// <summary>The words, trimmed; may be empty when <see cref="Content"/> carries attachments.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// Attachments, as on <c>ChatRequestDto.Content</c> (image and document parts with inline data; text parts are ignored):
    /// the same rules as a chat message's (<c>AI:Chat:Attachments</c>: types, sizes, <c>MaxCount</c>; a file that is not an
    /// image or a PDF only where the application's <c>ICliFileAttachmentPolicy</c> takes files). Stored with the queued message.
    /// </summary>
    public List<ContentPartDto>? Content { get; set; }

    /// <summary>The model chosen in the composer; null for the thread's default. Checked against the user's allowed models when it goes.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// The reasoning effort chosen in the composer (as on <c>ChatRequestDto.Effort</c>); null for none. Checked against the
    /// model's efforts when it goes. A message whose effort differs from the running turn's is not handed to that turn: it
    /// waits for the turn to end and goes as a turn of its own.
    /// </summary>
    public string? Effort { get; set; }

    /// <summary>The per-turn metadata the composer would have sent (same limits as <c>ChatRequestDto.Metadata</c>).</summary>
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

/// <summary>
/// A queued message's new words (<c>PUT chat/threads/{threadId}/queue/{messageId}</c>).
/// </summary>
public class UpdateQueuedMessageDto
{
    /// <summary>The words; required, trimmed.</summary>
    public string Text { get; set; } = string.Empty;
}
