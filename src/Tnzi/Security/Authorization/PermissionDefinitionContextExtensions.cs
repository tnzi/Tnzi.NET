namespace Tnzi.Security.Authorization;

/// <summary>
/// Convenience declarations over <see cref="IPermissionDefinitionContext"/>.
/// </summary>
public static class PermissionDefinitionContextExtensions
{
    /// <summary>
    /// Declare per-operation permission codes for one entity surface in a
    /// single call: <c>{prefix}.view</c> / <c>{prefix}.create</c> /
    /// <c>{prefix}.update</c> / <c>{prefix}.delete</c>, filtered by
    /// <paramref name="actions"/>. Display names derive from
    /// <paramref name="displayName"/> ("Users" → "View Users",
    /// "Create Users", …).
    /// </summary>
    /// <param name="context">The permission definition context.</param>
    /// <param name="prefix">Code prefix without the action segment, e.g. <c>user</c> or <c>finance.account</c>.</param>
    /// <param name="displayName">Human-readable plural entity name, e.g. <c>Users</c>.</param>
    /// <param name="parentName">Group (or parent permission) the codes belong to.</param>
    /// <param name="category">Category applied to every declared action code; null = inherit from the group.</param>
    /// <param name="actions">Which action codes to declare. Declare only what the surface exposes.</param>
    public static void AddCrudPermissions(
        this IPermissionDefinitionContext context,
        string prefix,
        string displayName,
        string? parentName = null,
        PermissionCategory? category = null,
        CrudActions actions = CrudActions.All)
    {
        Check.NotNull(context);
        Check.NotNullOrWhiteSpace(prefix);
        Check.NotNullOrWhiteSpace(displayName);
        if (actions == CrudActions.None)
        {
            throw new ArgumentException("At least one crud action must be declared.", nameof(actions));
        }

        if (actions.HasFlag(CrudActions.View))
        {
            context.AddPermission($"{prefix}.view", $"View {displayName}", parentName: parentName, category: category);
        }

        if (actions.HasFlag(CrudActions.Create))
        {
            context.AddPermission($"{prefix}.create", $"Create {displayName}", parentName: parentName, category: category);
        }

        if (actions.HasFlag(CrudActions.Update))
        {
            context.AddPermission($"{prefix}.update", $"Update {displayName}", parentName: parentName, category: category);
        }

        if (actions.HasFlag(CrudActions.Delete))
        {
            context.AddPermission($"{prefix}.delete", $"Delete {displayName}", parentName: parentName, category: category);
        }
    }
}
