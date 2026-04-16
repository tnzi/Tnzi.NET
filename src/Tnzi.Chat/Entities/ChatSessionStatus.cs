namespace Tnzi.Chat.Entities;

/// <summary>
/// Lifecycle status of a ChatSession grouping.
/// </summary>
public enum ChatSessionStatus
{
    /// <summary>
    /// Session is active and visible on user inboxes.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Session has been archived. Stays queryable by admins but is hidden
    /// from default user-facing list views.
    /// </summary>
    Archived = 2,
}
