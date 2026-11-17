/**
 * Submenu navigation by path: which level of a menu tree is shown when the
 * submenus along `path` have been opened in place, each under a back row.
 *
 * `TUserMenu` drills in place on touch and narrow screens (and inside a
 * fly-out, below its first level), and flies submenus out beside the menu
 * with a mouse; `TActionMenu` always drills in place. Both resolve the level
 * they show here.
 */

/** The shape submenu navigation needs: an id, and optional children. */
export interface MenuNode<T> {
  readonly id: string
  readonly children?: ReadonlyArray<T>
}

/** One level of the menu: the items to show, and the items opened to get here. */
export interface MenuLevel<T> {
  readonly items: ReadonlyArray<T>
  /** The items opened on the way down, outermost first; empty at the top. */
  readonly trail: readonly T[]
}

/**
 * The level `path` (the ids of the opened items, outermost first) leads to. A
 * path that no longer matches the items (they changed while the menu was open)
 * stops at the deepest level that still exists rather than showing nothing.
 */
export function resolveMenuLevel<T extends MenuNode<T>>(root: ReadonlyArray<T>, path: readonly string[]): MenuLevel<T> {
  let items = root
  const trail: T[] = []
  for (const id of path) {
    const next = items.find((item) => item.id === id)
    if (!next?.children?.length) break
    trail.push(next)
    items = next.children
  }
  return { items, trail }
}

/** Whether activating `item` opens a submenu rather than selecting it. */
export function opensSubmenu<T extends MenuNode<T>>(item: T): boolean {
  return (item.children?.length ?? 0) > 0
}
