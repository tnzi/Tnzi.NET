/**
 * Submenu navigation for `TUserMenu`.
 *
 * A submenu opens in place of the list it belongs to, with a back row at the
 * top, rather than flying out to the side. The account menu rises from the
 * sidebar footer inside a 300px column; a fly-out would cover the
 * conversation, and on a phone there is nowhere to fly out to. Drilling in
 * works the same with a mouse, a finger and a keyboard.
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
