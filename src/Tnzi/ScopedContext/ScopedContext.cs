namespace Tnzi.ScopedContext;

/// <summary>
/// 作用域上下文基类
/// </summary>
public abstract class ScopedContext : IScopedContext
{
    private readonly Dictionary<string, object> _items = new();

    public virtual string? CurrentUserId { get; set; }
    public virtual string? CurrentUserName { get; set; }
    public virtual IReadOnlyList<string> CurrentUserRoles { get; set; } = Array.Empty<string>();
    public virtual AuditOperationInfo? AuditOperation { get; set; }
    public virtual string? ClientIpAddress { get; set; }
    public virtual string? UserAgent { get; set; }
    public virtual string? TraceId { get; set; }

    public IDictionary<string, object> Items => _items;

    public virtual void SetItem<T>(string key, T value)
    {
        _items[key] = value!;
    }

    public virtual T? GetItem<T>(string key)
    {
        if (_items.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }
}

