import { inject, provide, type InjectionKey } from 'vue'

/**
 * Chrome-level commands the shell owns, handed down to whatever is rendering
 * the chrome for the current layout mode.
 *
 * There are exactly two, and both exist for the same reason: the thing that
 * OWNS the affordance and the thing that shows the BUTTON stopped being the
 * same component once a layout mode grew its own chrome.
 *
 *  - `openSearch` opens the global search modal, which `TAdminShell` mounts
 *    (and whose Ctrl/Cmd+K binding it owns). Every other layout mode reaches it
 *    from the header; the desktop taskbar is rendered inside the shell's
 *    default slot and cannot emit into it.
 *  - `openThemeDrawer` re-emits to the consumer, which owns the drawer. Same
 *    story.
 *
 * Deliberately not a store: these are functions bound to one mounted shell, and
 * two shells on one page (a preview inside a settings pane) must not fight over
 * a single global.
 */
export interface AdminShellActions {
  openSearch: () => void
  openThemeDrawer: () => void
}

export const ADMIN_SHELL_ACTIONS_KEY: InjectionKey<AdminShellActions> =
  Symbol('tnzi-admin-shell-actions')

export function provideAdminShellActions(actions: AdminShellActions): void {
  provide(ADMIN_SHELL_ACTIONS_KEY, actions)
}

/**
 * Read the shell's chrome commands.
 *
 * Returns no-ops outside a shell rather than throwing: a component that offers
 * a search button is still perfectly renderable in a story or a unit test with
 * no shell above it, and a dead button beats a mount that explodes.
 */
export function useAdminShellActions(): AdminShellActions {
  return inject(ADMIN_SHELL_ACTIONS_KEY, {
    openSearch: () => undefined,
    openThemeDrawer: () => undefined,
  })
}
