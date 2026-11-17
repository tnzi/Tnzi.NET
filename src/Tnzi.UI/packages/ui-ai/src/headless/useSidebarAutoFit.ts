/**
 * Sidebar auto-fit - fold the sidebar into its rail when the conversation has
 * too little room, and unfold it when there is enough again.
 *
 * ## What is measured
 *
 * Not the main area's width as it is, but the width the conversation would
 * have **with the sidebar expanded** (`conversationRoom`): the shell's width,
 * less the expanded sidebar, an open side panel (with its 70% cap) and an
 * artifact pane. That figure does not depend on whether the sidebar is
 * currently expanded, so folding it does not change the measurement that
 * folded it - the loop a "main area < N, collapse / main area > N, expand"
 * rule has (expanding takes the 250px the rail gave back, which puts the main
 * area under N again) cannot occur. `margin` still separates the two
 * thresholds (fold below `minWidth`, unfold at `minWidth + margin` or more),
 * which absorbs rounding and a side panel being dragged back and forth across
 * the line.
 *
 * ## Whose choice wins
 *
 * - Only an `expanded` sidebar is folded, and only a sidebar folded this way
 *   is unfolded (`useSidebarState().autoCollapsed`). A rail the user chose
 *   stays a rail at any width, and a hidden sidebar stays hidden.
 * - Folding and unfolding are not persisted (`applyAutoMode`), so the
 *   remembered preference is what the user last chose, not what the window
 *   last forced.
 * - A user who expands the sidebar while there is too little room keeps it
 *   expanded: the fold is held off until the room has been enough again at
 *   least once (after which a new squeeze folds it as usual).
 * - Below the mobile breakpoint the sidebar is a drawer and nothing here acts.
 */
import { watch, onScopeDispose, type WatchSource } from 'vue'
import type { UseSidebarStateReturn } from './useSidebarState'

/** Narrowest conversation column, in px, before the sidebar folds by itself. */
export const SIDEBAR_AUTO_FIT_MIN_WIDTH = 560
/** Extra room, in px, needed beyond the minimum before it unfolds again. */
export const SIDEBAR_AUTO_FIT_MARGIN = 80
/** The side panel never takes more than this share of the main area (`TAsidePanel`'s `max-width: 70%`). */
export const ASIDE_MAX_SHARE = 0.7

export interface ConversationRoomInput {
  /** Width of the whole shell (sidebar included). */
  readonly containerWidth: number
  /** Width of the expanded sidebar. */
  readonly sidebarWidth: number
  /** Width of the open side panel, 0 when closed. Capped at `ASIDE_MAX_SHARE` of the main area. */
  readonly asideWidth?: number
  /** Width of an open artifact pane, 0 when none. */
  readonly artifactWidth?: number
}

/** The conversation's width if the sidebar were expanded. */
export function conversationRoom(input: ConversationRoomInput): number {
  const main = Math.max(0, input.containerWidth - input.sidebarWidth)
  const aside = Math.min(input.asideWidth ?? 0, main * ASIDE_MAX_SHARE)
  return main - aside - (input.artifactWidth ?? 0)
}

export type SidebarFitAction = 'collapse' | 'expand' | null

export interface SidebarFitState {
  readonly room: number
  readonly mode: 'expanded' | 'icon' | 'hidden'
  readonly autoCollapsed: boolean
  /** The user expanded it while the room was short; no fold until the room recovers. */
  readonly held: boolean
  readonly minWidth?: number
  readonly margin?: number
}

/** What auto-fit does now, if anything. */
export function decideSidebarFit(state: SidebarFitState): SidebarFitAction {
  const min = state.minWidth ?? SIDEBAR_AUTO_FIT_MIN_WIDTH
  const margin = state.margin ?? SIDEBAR_AUTO_FIT_MARGIN
  if (state.mode === 'expanded' && !state.held && state.room < min) return 'collapse'
  if (state.mode === 'icon' && state.autoCollapsed && state.room >= min + margin) return 'expand'
  return null
}

export interface UseSidebarAutoFitOptions {
  /** The conversation's width with the sidebar expanded (see `conversationRoom`). */
  readonly room: WatchSource<number>
  /** Off: nothing folds, and a sidebar folded by auto-fit unfolds. Default on. */
  readonly enabled?: WatchSource<boolean>
  readonly minWidth?: WatchSource<number>
  readonly margin?: number
}

export interface UseSidebarAutoFitReturn {
  /** Whether a user's expand is holding off the fold. */
  readonly isHeld: () => boolean
  readonly dispose: () => void
}

function read<T>(source: WatchSource<T> | undefined, fallback: T): T {
  if (source === undefined) return fallback
  return typeof source === 'function' ? (source as () => T)() : (source as { value: T }).value
}

/** Applies `decideSidebarFit` to a sidebar whenever the room, the mode or the settings change. */
export function useSidebarAutoFit(
  sidebar: Pick<UseSidebarStateReturn, 'mode' | 'isMobile' | 'autoCollapsed' | 'choices' | 'applyAutoMode'>,
  options: UseSidebarAutoFitOptions,
): UseSidebarAutoFitReturn {
  const margin = options.margin ?? SIDEBAR_AUTO_FIT_MARGIN
  let held = false
  let seenChoices = sidebar.choices.value

  const minWidth = (): number => read(options.minWidth, SIDEBAR_AUTO_FIT_MIN_WIDTH)
  const enabled = (): boolean => read(options.enabled, true)
  const room = (): number => read(options.room, Number.POSITIVE_INFINITY)
  const apply = (action: 'icon' | 'expanded'): void => sidebar.applyAutoMode(action)

  function evaluate(): void {
    const mode = sidebar.mode.value
    const now = room()
    /* A choice of the user's since the last look: expanding while the room is
       short holds the fold off, any other choice releases it. Counted rather
       than read off a mode change, because the state changes the mode by
       itself too (back from the phone layout it restores the desktop mode),
       and that is not the user insisting on an expanded sidebar. */
    if (sidebar.choices.value !== seenChoices) {
      seenChoices = sidebar.choices.value
      held = !sidebar.isMobile.value && mode === 'expanded' && now < minWidth()
    }
    if (sidebar.isMobile.value) return
    if (!enabled()) {
      if (sidebar.autoCollapsed.value) apply('expanded')
      return
    }
    if (held && now >= minWidth() + margin) held = false
    const action = decideSidebarFit({
      room: now,
      mode,
      autoCollapsed: sidebar.autoCollapsed.value,
      held,
      minWidth: minWidth(),
      margin,
    })
    if (action === 'collapse') apply('icon')
    else if (action === 'expand') apply('expanded')
  }

  const stopInputs = watch(
    [() => room(), () => enabled(), () => minWidth(), sidebar.isMobile, sidebar.mode, sidebar.choices],
    evaluate,
    { immediate: true },
  )

  function dispose(): void {
    stopInputs()
  }
  onScopeDispose(dispose, true)

  return { isHeld: () => held, dispose }
}
