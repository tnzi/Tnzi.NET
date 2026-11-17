/**
 * What `TPageToolbar` decides without rendering: the shape of its actions and
 * how they fold into one "more" menu on a phone.
 */
import type { ActionMenuItem } from '../overlay/TActionMenu.vue'

/**
 * Attributes a page puts on an element the toolbar renders: its own `data-*`
 * test hooks, `aria-*` it needs, `id`, `name`. Bound before the toolbar's own
 * attributes, so the ones the toolbar depends on (`role`, `aria-haspopup`,
 * `data-menu-item`) cannot be overridden; `class` and `style` merge.
 */
export type ElementAttrs = Readonly<Record<string, string | number | boolean | null | undefined>>

/**
 * An icon action on the right of a page toolbar: a filter, a sort, a view
 * switch. With `items` it opens a menu of them (put `checked` on the current
 * choice); without, choosing it emits `action`.
 */
export interface PageToolbarAction {
  readonly id: string
  /** Accessible name and tooltip; the label of its row when folded on a phone. */
  readonly label: string
  /** Iconify name, e.g. `lucide:list-filter`, `lucide:arrow-down-up`. */
  readonly icon: string
  /** Something is applied (a filter other than the default): the button shows it. */
  readonly active?: boolean
  readonly disabled?: boolean
  /** The menu it opens. */
  readonly items?: ReadonlyArray<ActionMenuItem>
  /** Attributes for its button (and its row in the phone's "more" menu). */
  readonly attrs?: ElementAttrs
}

/**
 * The page's primary action, the last control of the toolbar: a small primary
 * button with a "+" icon by default. With `items` it opens a menu of ways to
 * create; with `split` as well, the button itself acts (`primary`) and only its
 * chevron opens the menu.
 */
export interface PageToolbarPrimary {
  readonly label: string
  /** Iconify name; `null` for none. Default `lucide:plus`. */
  readonly icon?: string | null
  readonly items?: ReadonlyArray<ActionMenuItem>
  readonly split?: boolean
  readonly disabled?: boolean
  readonly loading?: boolean
  /** Attributes for the primary button. */
  readonly attrs?: ElementAttrs
  /** Attributes for the chevron of a `split` primary. */
  readonly menuAttrs?: ElementAttrs
}

/** One option of a `TSegmented` (the tabs at the start of a page toolbar). */
export interface SegmentedOption<V extends string = string> {
  readonly value: V
  readonly label: string
  readonly icon?: string
  readonly disabled?: boolean
  /** How many things there wait on the person: a count at the option's right, shown only when above 0. */
  readonly badge?: number
  /** What a screen reader hears for the badge ("2 need your attention"); without it the count is not announced. */
  readonly badgeLabel?: string
  /**
   * A dot after the label: something there needs a look (a mailbox that failed
   * to sync), with no count to give. `true` is the warning colour, `'danger'`
   * the danger one, `'muted'` a quiet grey mark that only points one option
   * out (the default among several). Shown only when no `badge` is.
   */
  readonly dot?: boolean | 'warning' | 'danger' | 'muted'
  /** What a screen reader hears for the dot ("Sync failed"); without it the dot is not announced. */
  readonly dotLabel?: string
  /**
   * Something there is going on right now (a conversation's turn, a sync): a small turning ring
   * after the label, or on the icon's corner when the option is an icon alone. With a `dot` too,
   * the ring circles the dot (the dot says it needs a look, the ring that it is still moving).
   * Still under `prefers-reduced-motion`.
   */
  readonly running?: boolean
  /** What a screen reader hears for `running`. Default: the locale's `common.running` ("Working"). */
  readonly runningLabel?: string
  /** Attributes for the option's button (the page's own `data-*`), bound before the component's own. */
  readonly attrs?: ElementAttrs
}

export const DEFAULT_PRIMARY_ICON = 'lucide:plus'

/**
 * Folded entries carry their action's id in front of the item's, so two
 * actions' menus may reuse an item id ("all" in a filter and in a sort):
 * `<actionId>::<itemId>`, e.g. `filter::paused`. Printable on purpose, so a
 * page's own selector reaches a folded row (`[data-menu-item="filter::paused"]`).
 * An action id therefore must not contain `::`; an item id may.
 */
export const FOLDED_ID_SEPARATOR = '::'

/** The id of an action's menu item inside the folded menu. */
export function foldedItemId(actionId: string, itemId: string): string {
  return `${actionId}${FOLDED_ID_SEPARATOR}${itemId}`
}

/** Which action (and which of its items, if any) a folded menu id stands for. */
export function parseFoldedId(id: string): { actionId: string; itemId: string | null } {
  const at = id.indexOf(FOLDED_ID_SEPARATOR)
  return at < 0 ? { actionId: id, itemId: null } : { actionId: id.slice(0, at), itemId: id.slice(at + FOLDED_ID_SEPARATOR.length) }
}

function prefixed(actionId: string, items: ReadonlyArray<ActionMenuItem>): ActionMenuItem[] {
  return items.map((item) => ({
    ...item,
    id: foldedItemId(actionId, item.id),
    children: item.children ? prefixed(actionId, item.children) : undefined,
  }))
}

/**
 * The phone's "more" menu: one row per action, in order. An action with a
 * menu becomes a submenu (its current choice keeps its check); an active one
 * says so on the right.
 */
export function foldActions(actions: ReadonlyArray<PageToolbarAction>, activeMark: string): ActionMenuItem[] {
  return actions.map((action) => {
    if (action.id.includes(FOLDED_ID_SEPARATOR)) {
      throw new Error(`TPageToolbar: action id "${action.id}" contains "${FOLDED_ID_SEPARATOR}", which separates an action from its item in the folded menu.`)
    }
    return foldOne(action, activeMark)
  })
}

function foldOne(action: PageToolbarAction, activeMark: string): ActionMenuItem {
  return {
    id: action.id,
    label: action.label,
    icon: action.icon,
    disabled: action.disabled,
    value: action.active ? activeMark : undefined,
    children: action.items?.length ? prefixed(action.id, action.items) : undefined,
    attrs: action.attrs,
  }
}

/**
 * Whether the icon actions fold into one "more" menu: on a phone, and only
 * when there are two or more. A single action keeps its own button, since
 * folding it would only add a tap.
 */
export function shouldFoldActions(narrow: boolean, count: number): boolean {
  return narrow && count >= 2
}

/** The horizontal edges of a box, in the same coordinates for both arguments. */
export interface HorizontalBox {
  readonly left: number
  readonly right: number
}

/**
 * The `scrollLeft` that brings `target` fully into `container`'s visible part
 * (with `margin` to spare), or `null` when it is already in view. When the
 * target is wider than the container its start wins.
 */
export function scrollLeftToReveal(container: HorizontalBox, target: HorizontalBox, scrollLeft: number, margin = 8): number | null {
  if (target.left < container.left + margin) return Math.max(0, scrollLeft - (container.left + margin - target.left))
  if (target.right > container.right - margin) {
    const byEnd = scrollLeft + (target.right - (container.right - margin))
    const byStart = scrollLeft + (target.left - (container.left + margin))
    return Math.max(0, Math.min(byEnd, byStart))
  }
  return null
}
