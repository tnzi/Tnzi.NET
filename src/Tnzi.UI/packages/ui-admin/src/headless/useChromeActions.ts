import { computed, type ComputedRef } from 'vue'
import { useAdminShellConfig, type AdminChromeAction } from '../plugin/shell-config'

/**
 * A host chrome action resolved down to what a button needs to render: no
 * predicates left, no union types, nothing for a template to unwrap.
 */
export interface ResolvedChromeAction {
  key: string
  icon: string
  label: string
  active: boolean
  run: () => void
}

/**
 * The host's chrome actions (`defineAdminApp({ shell: { actions } })`),
 * filtered by their own `show` predicate and flattened for rendering.
 *
 * Extracted rather than inlined for the same reason `useSettingsEntry` was:
 * the strip that holds these buttons is rendered by two different components
 * (the sidebar footer, and - because the desktop layout has no sidebar - the
 * start-menu footer), and a second hand-written copy of the resolution rules
 * is how one of them ends up quietly disagreeing with the other.
 *
 * The predicates run INSIDE the computed, so an action gated on a permission
 * store appears and disappears as that store loads or the user changes -
 * a config object read once at install time could never do that on its own.
 */
export function useChromeActions(): ComputedRef<ResolvedChromeAction[]> {
  const config = useAdminShellConfig()

  return computed<ResolvedChromeAction[]>(() =>
    (config.actions ?? [])
      // Defensive: this list crosses the package boundary from a host app that
      // may not be TypeScript. A malformed entry is dropped rather than
      // rendered as a button that throws on click.
      .filter(
        (action): action is AdminChromeAction =>
          !!action && !!action.key && typeof action.onClick === 'function',
      )
      .filter((action) => resolve(action.show, true))
      .map((action) => ({
        key: action.key,
        icon: action.icon,
        label: typeof action.label === 'function' ? action.label() : (action.label ?? ''),
        active: resolve(action.active, false),
        run: action.onClick,
      })),
  )
}

function resolve(value: boolean | (() => boolean) | undefined, fallback: boolean): boolean {
  if (value === undefined) return fallback
  return typeof value === 'function' ? value() : value
}
