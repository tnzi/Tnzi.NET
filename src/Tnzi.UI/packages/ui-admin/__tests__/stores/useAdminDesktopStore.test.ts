import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import {
  useAdminDesktopStore,
  clampToSurface,
  routeToWindowInput,
  type AdminDesktopWindow,
} from '../../src/stores/useAdminDesktopStore'
import { MIN_VISIBLE, WINDOW_SIZE_PRESETS } from '../../src/headless/window-sizing'

/** Stand-in for "whatever a page with no hint opens at". */
const DEFAULT_WINDOW_SIZE = WINDOW_SIZE_PRESETS.wide

const SURFACE = { width: 1440, height: 800 }

function makeWindow(over: Partial<AdminDesktopWindow> = {}): AdminDesktopWindow {
  return {
    id: 'w1',
    title: 'identity.users',
    stack: [
      {
        name: 'identity.users',
        path: '/admin/identity/users',
        params: {},
        query: {},
        title: 'Users',
      },
    ],
    stackIndex: 0,
    x: 0,
    y: 0,
    width: DEFAULT_WINDOW_SIZE.width,
    height: DEFAULT_WINDOW_SIZE.height,
    minimized: false,
    maximized: false,
    z: 1,
    ...over,
  }
}

describe('useAdminDesktopStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('always mints a new window, so the same feature can be open twice', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'identity.users', path: '/admin/identity/users', title: 'Users' })
    const b = s.open({ name: 'identity.users', path: '/admin/identity/users', title: 'Users' })
    expect(a).not.toBe(b)
    expect(s.windows).toHaveLength(2)
    // Focus lands on the newest.
    expect(s.activeId).toBe(b)
  })

  it('focus raises a window above every other one', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    const b = s.open({ name: 'b', path: '/b', title: 'B' })
    expect(s.find(b)!.z).toBeGreaterThan(s.find(a)!.z)

    s.focus(a)
    expect(s.find(a)!.z).toBeGreaterThan(s.find(b)!.z)
    expect(s.stacked.at(-1)!.id).toBe(a)
  })

  it('focusing a minimized window restores it', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    s.minimize(a)
    expect(s.find(a)!.minimized).toBe(true)

    s.focus(a)
    expect(s.find(a)!.minimized).toBe(false)
    expect(s.activeId).toBe(a)
  })

  it('minimizing the focused window hands focus to the next visible one', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    const b = s.open({ name: 'b', path: '/b', title: 'B' })

    s.minimize(b)
    expect(s.activeId).toBe(a)
  })

  it('taskbar toggle minimizes the focused window and restores an unfocused one', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })

    s.toggleMinimize(a)
    expect(s.find(a)!.minimized).toBe(true)

    s.toggleMinimize(a)
    expect(s.find(a)!.minimized).toBe(false)
  })

  it('maximize remembers the pre-maximize geometry and restore puts it back', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    s.setGeometry(a, { x: 120, y: 90, width: 700, height: 500 })

    s.toggleMaximize(a, SURFACE)
    expect(s.find(a)).toMatchObject({ maximized: true, x: 0, y: 0, ...SURFACE })

    s.toggleMaximize(a, SURFACE)
    expect(s.find(a)).toMatchObject({
      maximized: false,
      x: 120,
      y: 90,
      width: 700,
      height: 500,
    })
  })

  // --- Navigation stack ------------------------------------------------------

  it('navigate pushes onto this window stack and retitles it', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'identity.users', path: '/admin/identity/users', title: 'Users' })

    s.navigate(a, {
      name: 'identity.users.detail',
      path: '/admin/identity/users/7',
      title: 'User Detail',
      params: { id: '7' },
    })

    const win = s.find(a)!
    expect(win.stack).toHaveLength(2)
    expect(win.stackIndex).toBe(1)
    expect(win.title).toBe('User Detail')
    expect(s.locationOf(a)).toMatchObject({ name: 'identity.users.detail', params: { id: '7' } })
  })

  it('replace swaps the current entry without growing the stack', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })

    s.replace(a, { name: 'a', path: '/a', title: 'A', query: { section: 'tools' } })

    expect(s.find(a)!.stack).toHaveLength(1)
    expect(s.locationOf(a)!.query).toEqual({ section: 'tools' })
  })

  it('back / forward walk this window own stack', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    s.navigate(a, { name: 'b', path: '/b', title: 'B' })

    expect(s.canGoBack(a)).toBe(true)
    s.back(a)
    expect(s.locationOf(a)!.name).toBe('a')
    expect(s.canGoBack(a)).toBe(false)

    s.forward(a)
    expect(s.locationOf(a)!.name).toBe('b')
  })

  it('navigating after going back discards the forward branch, like browser history', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    s.navigate(a, { name: 'b', path: '/b', title: 'B' })
    s.back(a)

    s.navigate(a, { name: 'c', path: '/c', title: 'C' })

    const win = s.find(a)!
    expect(win.stack.map((l) => l.name)).toEqual(['a', 'c'])
    expect(win.stackIndex).toBe(1)
  })

  it('carries the title back and forward with the stack entry', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'identity.users', path: '/u', title: 'Users' })
    s.navigate(a, { name: 'identity.users.detail', path: '/u/1', title: 'Detail', params: { id: '1' } })

    // The detail page reports the record's name once it loads.
    s.setTitle(a, 'Bizadmin')
    expect(s.find(a)!.title).toBe('Bizadmin')

    s.back(a)
    // Back must restore the LIST's title. Holding one title per window left the
    // record's name stranded on the list page.
    expect(s.find(a)!.title).toBe('Users')

    s.forward(a)
    // ...and the record keeps its own name for the return trip.
    expect(s.find(a)!.title).toBe('Bizadmin')
  })

  it('two windows keep independent navigation stacks', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'identity.users', path: '/u', title: 'Users' })
    const b = s.open({ name: 'identity.users', path: '/u', title: 'Users' })

    s.navigate(a, { name: 'identity.users.detail', path: '/u/1', title: 'A', params: { id: '1' } })
    s.navigate(b, { name: 'identity.users.detail', path: '/u/2', title: 'B', params: { id: '2' } })

    expect(s.locationOf(a)!.params).toEqual({ id: '1' })
    expect(s.locationOf(b)!.params).toEqual({ id: '2' })

    s.back(a)
    expect(s.locationOf(a)!.name).toBe('identity.users')
    // B is untouched by A's back.
    expect(s.locationOf(b)!.params).toEqual({ id: '2' })
  })

  // --- Closing ---------------------------------------------------------------

  it('closing the focused window hands focus to the frontmost remaining one', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    const b = s.open({ name: 'b', path: '/b', title: 'B' })
    s.focus(a)

    s.close(a)
    expect(s.windows).toHaveLength(1)
    expect(s.activeId).toBe(b)
  })

  it('closing the last window clears focus', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    s.close(a)
    expect(s.activeId).toBeNull()
  })

  // --- Geometry --------------------------------------------------------------

  it('clampToSurface keeps a window reachable when the viewport shrank', () => {
    // Persisted from a wide monitor; entirely off-canvas on a laptop.
    const stranded = makeWindow({ x: 1800, y: 1200 })
    const fixed = clampToSurface(stranded, SURFACE)

    // Not merely "inside the surface" - enough of it must be inside to grab.
    expect(fixed.x).toBeLessThanOrEqual(SURFACE.width - MIN_VISIBLE)
    expect(fixed.y).toBeLessThanOrEqual(SURFACE.height - MIN_VISIBLE)
    expect(fixed.width).toBeLessThanOrEqual(SURFACE.width)
    expect(fixed.height).toBeLessThanOrEqual(SURFACE.height)
  })

  it('clampToSurface pins the title bar below the top edge', () => {
    // Above the top edge the title bar - the only drag handle - is gone
    // entirely, so `y` is hard-bounded. `x` is not: hanging a wide window off
    // the left to read its right-hand columns is legitimate, and MIN_VISIBLE
    // keeps it grabbable.
    const fixed = clampToSurface(makeWindow({ x: -500, y: -400 }), SURFACE)
    expect(fixed.y).toBe(0)
    expect(fixed.x).toBeGreaterThanOrEqual(MIN_VISIBLE - fixed.width)
    expect(fixed.x + fixed.width).toBeGreaterThanOrEqual(MIN_VISIBLE)
  })

  it('clampAll keeps maximized windows filling the new surface', () => {
    const s = useAdminDesktopStore()
    const a = s.open({ name: 'a', path: '/a', title: 'A' })
    s.toggleMaximize(a, SURFACE)

    const smaller = { width: 900, height: 600 }
    s.clampAll(smaller)

    expect(s.find(a)).toMatchObject({ x: 0, y: 0, ...smaller })
  })
})

describe('routeToWindowInput', () => {
  it('carries the size hint off the route meta', () => {
    // The whole reason this helper exists: four call sites were hand-building
    // this object and three had quietly dropped `size`, so a route's
    // `meta.window` only applied when opened from the desktop icon grid.
    const input = routeToWindowInput({
      name: 'user-center',
      path: '/admin/user-center',
      meta: { title: 'Profile', icon: 'mdi:account', window: { preset: 'medium' } },
    })

    expect(input).toEqual({
      name: 'user-center',
      path: '/admin/user-center',
      title: 'Profile',
      icon: 'mdi:account',
      size: { preset: 'medium' },
    })
  })

  it('stores only the declared icon, leaving the fallback to render time', () => {
    // Windows are persisted. Resolving the shared icon map HERE would freeze
    // today's glyph into localStorage, so a restored taskbar would keep showing
    // an icon the sidebar has since changed. `resolveWindowIcon` derives it.
    expect(routeToWindowInput({ name: 'settings', path: '/s' }).icon).toBeUndefined()
    expect(
      routeToWindowInput({ name: 'settings', path: '/s', meta: { icon: 'mdi:custom' } }).icon,
    ).toBe('mdi:custom')
  })

  it('falls back to the supplied label, then the route name', () => {
    // Menu entries carry an already-translated label; a route with neither
    // still has to name its window somehow.
    expect(routeToWindowInput({ name: 'blog', path: '/blog' }, 'Blog').title).toBe('Blog')
    expect(routeToWindowInput({ name: 'blog', path: '/blog' }).title).toBe('blog')
  })

  it('opens a hinted route at the hinted size', () => {
    const s = useAdminDesktopStore()
    s.setSurface(SURFACE)
    const { id } = s.openOrFocusRoute(
      routeToWindowInput({ name: 'user-center', path: '/u', meta: { window: { preset: 'medium' } } }),
    )

    expect(s.find(id)).toMatchObject(WINDOW_SIZE_PRESETS.medium)
  })

  it('resets a hinted window to ITS default, not the generic one', () => {
    // A node-graph editor asked for `full`; handing it back the table-shaped
    // default would be a reset that undoes the page's own decision.
    const s = useAdminDesktopStore()
    s.setSurface(SURFACE)
    const { id } = s.openOrFocusRoute(
      routeToWindowInput({ name: 'editor', path: '/e', meta: { window: { preset: 'compact' } } }),
    )
    s.setGeometry(id, { width: 1000, height: 700 })
    s.resetLayout()

    expect(s.find(id)).toMatchObject(WINDOW_SIZE_PRESETS.compact)
  })
})

describe('panel windows', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('keeps a panel apart from the module that shares its name', () => {
    const s = useAdminDesktopStore()
    s.setSurface(SURFACE)

    // The chat PANEL (the IM surface) and the chat MODULE (its admin pages) are
    // different things that happen to share a word. Keyed together, opening the
    // module's pages would focus the IM window instead of opening anything.
    const panel = s.openOrFocusRoute({ name: 'chat-panel', path: '', title: 'Messages', panel: 'chat' })
    const module = s.openOrFocusRoute({ name: 'chat.overview', path: '/chat', title: 'Overview' })

    expect(panel.minted).toBe(true)
    expect(module.minted).toBe(true)
    expect(panel.id).not.toBe(module.id)
    expect(s.windows).toHaveLength(2)
  })

  it('focuses the panel instead of opening a second copy', () => {
    const s = useAdminDesktopStore()
    s.setSurface(SURFACE)
    const first = s.openOrFocusRoute({ name: 'chat-panel', path: '', title: 'Messages', panel: 'chat' })
    s.openOrFocusRoute({ name: 'other', path: '/other', title: 'Other' })
    const again = s.openOrFocusRoute({ name: 'chat-panel', path: '', title: 'Messages', panel: 'chat' })

    expect(again).toEqual({ id: first.id, minted: false })
    expect(s.activeId).toBe(first.id)
  })

  it('does NOT store a tile colour on the window', () => {
    const s = useAdminDesktopStore()
    const { id } = s.openOrFocusRoute({ name: 'chat-panel', path: '', title: 'Messages', panel: 'chat' })

    // The panel's colour is declared on its registration and resolved at render
    // (`getDesktopPanelTint`). Written into the record it would freeze today's
    // brand value into localStorage: change it in a later version and every
    // already-open window keeps the old one. Same reason the icon is not baked.
    expect(s.find(id)?.stack[0]).not.toHaveProperty('color')
  })

  it('carries the panel marker into the window, so the host can render it', () => {
    const s = useAdminDesktopStore()
    const { id } = s.openOrFocusRoute({ name: 'chat-panel', path: '', title: 'Messages', panel: 'chat' })
    expect(s.find(id)?.stack[0].panel).toBe('chat')
  })
})
