import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import TDesktopWindow from '../../../src/components/desktop/TDesktopWindow.vue'
import TDesktopWindowHost from '../../../src/components/desktop/TDesktopWindowHost.vue'
import TDesktopIcons from '../../../src/components/desktop/TDesktopIcons.vue'
import TDesktopTaskbar from '../../../src/components/desktop/TDesktopTaskbar.vue'
import TDesktopHost from '../../../src/components/desktop/TDesktopHost.vue'
import { registerDesktopPanel, unregisterDesktopPanel } from '../../../src/headless/desktop-panels'
import { useAdminDesktopStore } from '../../../src/stores/useAdminDesktopStore'
import { useAdminRouteStore, type AdminMenuItem } from '../../../src/stores/useAdminRouteStore'
import { useAdminThemeStore } from '../../../src/stores/useAdminThemeStore'

const Blank = defineComponent({ render: () => h('div', { class: 'page-body' }) })

/** Mirrors a real detail page: reads its record id from PROPS, not useRoute(). */
const PropsPage = defineComponent({
  props: { id: { type: String, default: '' } },
  render(this: { id: string }) {
    return h('div', { class: 'page-body', 'data-id': this.id })
  },
})

/** Naive pieces the desktop chrome renders, stubbed so no provider is needed. */
const stubs = {
  NResult: { props: ['title', 'description'], template: '<div class="n-result">{{ title }}</div>' },
  NSpin: { template: '<div class="n-spin" />' },
  NDropdown: { template: '<div class="n-dropdown" />' },
  NPopover: { template: '<div class="n-popover"><slot name="trigger" /><slot /></div>' },
  NInput: { template: '<input class="n-input" />' },
  NScrollbar: { template: '<div class="n-scrollbar"><slot /></div>' },
  TSvgIcon: { props: ['icon'], template: '<i class="t-icon" :data-icon="icon" />' },
}

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: Blank },
      { path: '/users', name: 'identity.users', component: Blank, meta: { title: 'Users' } },
      { path: '/roles', name: 'identity.roles', component: Blank, meta: { title: 'Roles' } },
      {
        path: '/users/:id',
        name: 'identity.users.detail',
        component: PropsPage,
        props: true,
        meta: { title: 'User Detail' },
      },
    ],
  })
}

const SURFACE = { width: 1440, height: 800 }

describe('TDesktopWindow', () => {
  let router: Router

  beforeEach(async () => {
    setActivePinia(createPinia())
    router = makeRouter()
    await router.push('/')
    await router.isReady()
  })

  it('hides a minimized window WITHOUT unmounting the page inside it', async () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mount(TDesktopWindow, {
      props: { win: desktop.find(id)!, surface: SURFACE, active: true },
      global: { plugins: [router], stubs },
    })

    expect(wrapper.find('.t-desktop-window').attributes('style')).not.toContain(
      'display: none',
    )

    desktop.minimize(id)
    await wrapper.setProps({ win: desktop.find(id)! })
    await nextTick()

    const frame = wrapper.find('.t-desktop-window')
    // `v-show`, not `v-if`: the element is still in the DOM, merely hidden.
    // Unmounting here would silently discard a half-filled form every time
    // someone parked a window on the taskbar.
    //
    // Asserted on the inline style rather than `isVisible()`: VTU mounts
    // detached from the document, where that helper reports everything visible.
    expect(frame.exists()).toBe(true)
    expect(frame.attributes('style')).toContain('display: none')
    expect(wrapper.find('.t-desktop-window-host').exists()).toBe(true)
  })

  it('closes / minimizes / maximizes from the title bar controls', async () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mount(TDesktopWindow, {
      props: { win: desktop.find(id)!, surface: SURFACE, active: true },
      global: { plugins: [router], stubs },
    })

    const buttons = wrapper.findAll('.t-desktop-window__btn')
    expect(buttons).toHaveLength(3)

    await buttons[0].trigger('click')
    expect(desktop.find(id)!.minimized).toBe(true)

    await buttons[1].trigger('click')
    expect(desktop.find(id)!.maximized).toBe(true)

    await buttons[2].trigger('click')
    expect(desktop.find(id)).toBeUndefined()
  })

  it('drags by the title bar and stays inside the surface', async () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })
    desktop.setGeometry(id, { x: 100, y: 100 })

    const wrapper = mount(TDesktopWindow, {
      props: { win: desktop.find(id)!, surface: SURFACE, active: true },
      global: { plugins: [router], stubs },
    })

    await wrapper.find('.t-desktop-window__bar').trigger('mousedown', {
      button: 0,
      clientX: 200,
      clientY: 200,
    })
    window.dispatchEvent(new MouseEvent('mousemove', { clientX: 260, clientY: 240 }))
    window.dispatchEvent(new MouseEvent('mouseup'))

    expect(desktop.find(id)).toMatchObject({ x: 160, y: 140 })
  })

  it('does not drag a maximized window', async () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })
    desktop.toggleMaximize(id, SURFACE)

    const wrapper = mount(TDesktopWindow, {
      props: { win: desktop.find(id)!, surface: SURFACE, active: true },
      global: { plugins: [router], stubs },
    })

    await wrapper.find('.t-desktop-window__bar').trigger('mousedown', {
      button: 0,
      clientX: 200,
      clientY: 200,
    })
    window.dispatchEvent(new MouseEvent('mousemove', { clientX: 400, clientY: 400 }))
    window.dispatchEvent(new MouseEvent('mouseup'))

    expect(desktop.find(id)).toMatchObject({ x: 0, y: 0 })
  })

  it('drops the resize grips while maximized', () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mount(TDesktopWindow, {
      props: { win: desktop.find(id)!, surface: SURFACE, active: true },
      global: { plugins: [router], stubs },
    })
    expect(wrapper.findAll('.t-desktop-window__grip')).toHaveLength(8)

    desktop.toggleMaximize(id, SURFACE)
    return wrapper.setProps({ win: desktop.find(id)! }).then(() => {
      expect(wrapper.findAll('.t-desktop-window__grip')).toHaveLength(0)
    })
  })
})

describe('TDesktopWindowHost', () => {
  let router: Router

  beforeEach(async () => {
    setActivePinia(createPinia())
    router = makeRouter()
    await router.push('/')
    await router.isReady()
  })

  it('mounts the page resolved from the route table', async () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    const wrapper = mount(TDesktopWindowHost, {
      props: { windowId: id },
      global: { plugins: [router], stubs },
    })
    await nextTick()
    await nextTick()

    expect(wrapper.find('.page-body').exists()).toBe(true)
  })

  it('passes route params as props when the record declares props: true', async () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({
      name: 'identity.users.detail',
      path: '/users/42',
      title: 'Detail',
      params: { id: '42' },
    })

    const wrapper = mount(TDesktopWindowHost, {
      props: { windowId: id },
      global: { plugins: [router], stubs },
    })
    await nextTick()
    await nextTick()

    // `RouterView` does this; mounting the component bare hands it `undefined`
    // for every param. Measured in the browser as a request to
    // `/api/admin/users/undefined` while the window sat on "Loading..."
    expect(wrapper.find('.page-body').attributes('data-id')).toBe('42')
  })

  it('refuses a window whose route the user may no longer open', async () => {
    const desktop = useAdminDesktopStore()
    const routeStore = useAdminRouteStore()
    const id = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    // A window is restored from localStorage, which outlives a permission
    // change and which the user can edit. The route guard never runs for it,
    // so this check is the only thing standing between a stale persisted
    // window and a page its owner is no longer allowed to see.
    vi.spyOn(routeStore, 'deniedRouteNames', 'get').mockReturnValue(
      new Set(['identity.users']),
    )

    const wrapper = mount(TDesktopWindowHost, {
      props: { windowId: id },
      global: { plugins: [router], stubs },
    })
    await nextTick()

    expect(wrapper.find('.page-body').exists()).toBe(false)
    expect(wrapper.find('.n-result').exists()).toBe(true)
  })

  it('refuses a window into a module the backend did not load', async () => {
    const desktop = useAdminDesktopStore()
    const routeStore = useAdminRouteStore()
    const id = desktop.open({ name: 'identity.users', path: '/users', title: 'Users' })

    vi.spyOn(routeStore, 'unavailableRouteNames', 'get').mockReturnValue(
      new Set(['identity.users']),
    )

    const wrapper = mount(TDesktopWindowHost, {
      props: { windowId: id },
      global: { plugins: [router], stubs },
    })
    await nextTick()

    expect(wrapper.find('.page-body').exists()).toBe(false)
    expect(wrapper.find('.n-result').exists()).toBe(true)
  })

  it('reports a missing page instead of rendering an empty window', async () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({ name: 'gone.away', path: '/gone', title: 'Gone' })

    const wrapper = mount(TDesktopWindowHost, {
      props: { windowId: id },
      global: { plugins: [router], stubs },
    })
    await nextTick()

    expect(wrapper.find('.n-result').exists()).toBe(true)
  })
})

describe('TDesktopIcons', () => {
  beforeEach(() => setActivePinia(createPinia()))

  function seedMenus(items: AdminMenuItem[]): void {
    const routeStore = useAdminRouteStore()
    vi.spyOn(routeStore, 'menus', 'get').mockReturnValue(items)
  }

  it('renders one icon per top-level entry, not one per page', () => {
    seedMenus([
      {
        key: 'identity',
        label: 'Identity',
        path: '/identity',
        children: [
          { key: 'identity.users', label: 'Users', path: '/identity/users' },
          { key: 'identity.roles', label: 'Roles', path: '/identity/roles' },
        ],
      },
      { key: 'dashboard', label: 'Dashboard', path: '/dashboard' },
    ])

    const wrapper = mount(TDesktopIcons, { global: { stubs } })
    const labels = wrapper.findAll('.t-desktop-icons__label').map((n) => n.text())

    // A module is ONE tile; its pages live in the window's own nav. Flattening
    // put a hundred-odd tiles on the wallpaper, which is a launcher.
    expect(labels).toEqual(['Identity', 'Dashboard'])
  })

  it('drops a top-level entry with no page anywhere beneath it', () => {
    seedMenus([
      // Labels all the way down: opening it would mount an empty window.
      { key: 'empty', label: 'Empty', path: '', children: [{ key: 'empty.a', label: 'A', path: '' }] },
      { key: 'dashboard', label: 'Dashboard', path: '/dashboard' },
    ])

    const wrapper = mount(TDesktopIcons, { global: { stubs } })
    expect(wrapper.findAll('.t-desktop-icons__label').map((n) => n.text())).toEqual(['Dashboard'])
  })

  it('sums the counts hidden behind a module tile', () => {
    seedMenus([
      {
        key: 'ops',
        label: 'Ops',
        path: '/ops',
        children: [
          { key: 'ops.a', label: 'A', path: '/ops/a', badge: 3 },
          { key: 'ops.b', label: 'B', path: '/ops/b', badge: 4 },
          { key: 'ops.c', label: 'C', path: '/ops/c' },
        ],
      },
    ])

    const wrapper = mount(TDesktopIcons, { global: { stubs } })
    // The sidebar does not aggregate because it shows the children inline; here
    // they are hidden behind one tile, so a per-page count would only become
    // visible after opening the module - too late to be the reason you opened it.
    expect(wrapper.find('.t-desktop-icons__badge').text()).toBe('7')
  })

  it('lets a module carry its own count instead of the sum', () => {
    seedMenus([
      {
        key: 'ops',
        label: 'Ops',
        path: '/ops',
        badge: 2,
        children: [{ key: 'ops.a', label: 'A', path: '/ops/a', badge: 9 }],
      },
    ])

    const wrapper = mount(TDesktopIcons, { global: { stubs } })
    expect(wrapper.find('.t-desktop-icons__badge').text()).toBe('2')
  })

  it('opens on double click, not on the first click', async () => {
    seedMenus([{ key: 'identity.users', label: 'Users', path: '/identity/users' }])

    const wrapper = mount(TDesktopIcons, { global: { stubs } })
    const icon = wrapper.find('.t-desktop-icons__item')

    await icon.trigger('click')
    expect(wrapper.emitted('open')).toBeUndefined()

    await icon.trigger('dblclick')
    expect(wrapper.emitted('open')).toHaveLength(1)
  })

  it('derives the column height from the border box, not the content box', async () => {
    // The bug this guards: a horizontal scrollbar is drawn inside the content
    // box, so `clientHeight` drops the moment one appears. Deriving rows from
    // it means bar appears -> one fewer row -> one more column -> every icon
    // moves, and a second double-click at the same coordinates opens a
    // different page. Measured in a browser: clientHeight fell 10px while
    // offsetHeight and the row count held.
    seedMenus([{ key: 'a', label: 'A', path: '/a' }])

    // Drive the component's own observer rather than a resize event - the
    // resize listener is only the no-ResizeObserver fallback path.
    let fire: () => void = () => undefined
    const original = globalThis.ResizeObserver
    globalThis.ResizeObserver = class {
      constructor(cb: () => void) {
        fire = cb
      }
      observe(): void {}
      disconnect(): void {}
      unobserve(): void {}
    } as unknown as typeof ResizeObserver

    const wrapper = mount(TDesktopIcons, { global: { stubs }, attachTo: document.body })
    const grid = wrapper.find('.t-desktop-icons').element as HTMLElement

    // Border box stays put; content box loses the scrollbar's height.
    Object.defineProperty(grid, 'offsetHeight', { value: 800, configurable: true })
    Object.defineProperty(grid, 'clientHeight', { value: 780, configurable: true })
    fire()
    await nextTick()
    const withBar = wrapper.find('.t-desktop-icons').attributes('style')

    // Same element, no bar: content box now equals the border box.
    Object.defineProperty(grid, 'clientHeight', { value: 800, configurable: true })
    fire()
    await nextTick()
    const withoutBar = wrapper.find('.t-desktop-icons').attributes('style')
    globalThis.ResizeObserver = original

    expect(withBar).toBe(withoutBar)
    // 800 - 20 padding - 12 reserve = 768 -> floor(770 / 94) = 8 rows.
    expect(withBar).toContain('repeat(8,')
    wrapper.unmount()
  })

  it('paints a badge only when the menu entry carries a count', () => {
    seedMenus([
      { key: 'a', label: 'A', path: '/a', badge: 3 },
      { key: 'b', label: 'B', path: '/b', badge: 0 },
      { key: 'c', label: 'C', path: '/c' },
    ])

    const wrapper = mount(TDesktopIcons, { global: { stubs } })
    const badges = wrapper.findAll('.t-desktop-icons__badge')

    // Zero paints nothing - the same rule the sidebar applies.
    expect(badges).toHaveLength(1)
    expect(badges[0].text()).toBe('3')
  })
})

describe('TDesktopTaskbar', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('renders one button per open window, in open order', () => {
    const desktop = useAdminDesktopStore()
    desktop.open({ name: 'a', path: '/a', title: 'Alpha' })
    desktop.open({ name: 'b', path: '/b', title: 'Beta' })
    // Focusing the first must NOT reorder the buttons - they would rearrange
    // themselves exactly when the user is reaching for one.
    desktop.focus(desktop.windows[0].id)

    const wrapper = mount(TDesktopTaskbar, { global: { stubs } })
    // The buttons are icon tiles - the title reaches the user through the
    // tooltip and the accessible name, not an inline label.
    const labels = wrapper
      .findAll('.t-desktop-taskbar__win')
      .map((n) => n.attributes('aria-label'))

    expect(labels).toEqual(['Alpha', 'Beta'])
  })

  it('hides the search button when the app turned global search off', async () => {
    const theme = useAdminThemeStore()
    theme.globalSearchVisible = false

    const wrapper = mount(TDesktopTaskbar, { global: { stubs } })
    // Same gate the header uses - an app that hid search must not get it back
    // through the dock.
    expect(wrapper.find('.t-desktop-taskbar__search').exists()).toBe(false)

    theme.globalSearchVisible = true
    await nextTick()
    expect(wrapper.find('.t-desktop-taskbar__search').exists()).toBe(true)
  })

  it('measures the strip, not its contents, when deciding to show titles', async () => {
    const desktop = useAdminDesktopStore()
    desktop.open({ name: 'a', path: '/a', title: 'Alpha' })
    desktop.open({ name: 'b', path: '/b', title: 'Beta' })

    let fire: () => void = () => undefined
    const original = globalThis.ResizeObserver
    globalThis.ResizeObserver = class {
      constructor(cb: () => void) {
        fire = cb
      }
      observe(): void {}
      disconnect(): void {}
      unobserve(): void {}
    } as unknown as typeof ResizeObserver

    const wrapper = mount(TDesktopTaskbar, { global: { stubs }, attachTo: document.body })
    const strip = wrapper.find('.t-desktop-taskbar__windows').element as HTMLElement

    // Room for two labelled buttons.
    Object.defineProperty(strip, 'clientWidth', { value: 600, configurable: true })
    fire()
    await nextTick()
    expect(wrapper.findAll('.t-desktop-taskbar__win-label')).toHaveLength(2)

    // Squeezed: titles go, icons stay. Measured against the strip's own width -
    // the flex leftover - because a content-sized box would shrink when the
    // labels came off and then "fit" them again, forever.
    Object.defineProperty(strip, 'clientWidth', { value: 120, configurable: true })
    fire()
    await nextTick()
    expect(wrapper.findAll('.t-desktop-taskbar__win-label')).toHaveLength(0)
    expect(wrapper.findAll('.t-desktop-taskbar__win')).toHaveLength(2)

    globalThis.ResizeObserver = original
    wrapper.unmount()
  })

  it('widens the running indicator for the focused window only', () => {
    const desktop = useAdminDesktopStore()
    desktop.open({ name: 'a', path: '/a', title: 'Alpha' })
    const second = desktop.open({ name: 'b', path: '/b', title: 'Beta' })
    desktop.focus(second)

    const wrapper = mount(TDesktopTaskbar, { global: { stubs } })
    const widths = wrapper
      .findAll('.t-desktop-taskbar__indicator')
      .map((n) => n.attributes('style'))

    // Open-but-unfocused stays narrow; a bare present/absent dot would answer
    // "is it open" but not "which one am I looking at".
    expect(widths[0]).toContain('8px')
    expect(widths[1]).toContain('16px')
  })

  it('tints a window button by the module its route belongs to', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setAuthRoutes([
      {
        name: 'shop',
        path: '/shop',
        meta: { title: 'Shop' },
        children: [
          { name: 'shop-orders', path: 'orders', meta: { title: 'Orders' } },
          { name: 'shop-products', path: 'products', meta: { title: 'Products' } },
        ],
      },
    ])
    const desktop = useAdminDesktopStore()
    desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })
    desktop.open({ name: 'shop-products', path: '/shop/products', title: 'Products' })

    const wrapper = mount(TDesktopTaskbar, { global: { stubs } })
    const tiles = wrapper
      .findAll('.t-desktop-taskbar__win-tile')
      .map((n) => n.attributes('style'))

    // Sibling pages of one module share a colour even though their route names
    // share no dotted prefix - the menu tree, not the name, decides the module.
    expect(tiles[0]).toBe(tiles[1])
  })

  it('toggles a window between focused and minimized', async () => {
    const desktop = useAdminDesktopStore()
    const id = desktop.open({ name: 'a', path: '/a', title: 'Alpha' })

    const wrapper = mount(TDesktopTaskbar, { global: { stubs } })
    const button = wrapper.find('.t-desktop-taskbar__win')

    await button.trigger('click')
    expect(desktop.find(id)!.minimized).toBe(true)

    await button.trigger('click')
    expect(desktop.find(id)!.minimized).toBe(false)
  })
})

describe('TDesktopHost wallpaper layers', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('spans the wallpaper across the taskbar, not just the surface above it', () => {
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/', name: 'home', component: Blank }] })
    const wrapper = mount(TDesktopHost, { global: { plugins: [router], stubs } })

    const desk = wrapper.find('.t-desktop')
    const layers = ['.t-desktop__glow--1', '.t-desktop__glow--2', '.t-desktop__photo', '.t-desktop__scrim']

    for (const sel of layers) {
      const el = desk.element.querySelector(sel)
      expect(el, `${sel} should exist`).not.toBeNull()
      // Direct child of the desktop root, so `inset: 0` covers the taskbar too.
      expect(el?.parentElement?.classList.contains('t-desktop')).toBe(true)
      // NOT inside the surface: that is where they used to live, and the
      // surface is a flex sibling that stops at the taskbar's top edge - so the
      // bar's `backdrop-filter` had only the root's flat fill to work with, and
      // a blurred flat colour is the same flat colour. The glass was inert at
      // every setting, which is exactly how it was reported.
      const surface = desk.element.querySelector('.t-desktop__surface')
      expect(surface?.contains(el ?? null)).toBe(false)
    }
  })
})

describe('TDesktopHost window order', () => {
  beforeEach(() => setActivePinia(createPinia()))

  const mountHost = () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', name: 'home', component: Blank }],
    })
    return mount(TDesktopHost, { global: { plugins: [router], stubs } })
  }
  const titles = (w: ReturnType<typeof mountHost>) =>
    w.findAll('.t-desktop-window').map((n) => n.find('.t-desktop-window__title').text())

  it('keeps DOM order stable when focus changes', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'a', path: '/a', title: 'Alpha' })
    const b = desktop.open({ name: 'b', path: '/b', title: 'Beta' })
    const wrapper = mountHost()
    const before = titles(wrapper)

    // Focus B first, then A, so the z-sorted order (Beta, Alpha) DIFFERS from
    // the open order. Focusing A last and asserting is vacuous: both orders
    // agree, and the test passes with the sorted list back in place.
    desktop.focus(b)
    await nextTick()
    desktop.focus(a)
    await nextTick()

    // Painting order is settled by each window's own inline `z-index`, so
    // sorting the DOM buys nothing - and reordering MOVES the nodes, which
    // restarts their CSS animations. Measured in Chrome: one focus change
    // replayed the open animation on BOTH windows, seen as the window being
    // sent to the back flickering once.
    expect(titles(wrapper)).toEqual(before)
    expect(before).toEqual(['Alpha', 'Beta'])
  })

  it('still lets z-index decide what is in front', async () => {
    const desktop = useAdminDesktopStore()
    const a = desktop.open({ name: 'a', path: '/a', title: 'Alpha' })
    desktop.open({ name: 'b', path: '/b', title: 'Beta' })
    const wrapper = mountHost()

    desktop.focus(a)
    await nextTick()

    const alpha = wrapper
      .findAll('.t-desktop-window')
      .find((n) => n.find('.t-desktop-window__title').text() === 'Alpha')
    const z = Number(alpha?.attributes('style')?.match(/z-index:\s*(\d+)/)?.[1])

    expect(z).toBe(Math.max(...desktop.windows.map((w) => w.z)))
  })
})

describe('TDesktopWindowHost panels', () => {
  beforeEach(() => setActivePinia(createPinia()))
  afterEach(() => unregisterDesktopPanel('late'))

  it('picks up a panel registered AFTER the window rendered', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', name: 'home', component: Blank }],
    })
    const desktop = useAdminDesktopStore()
    desktop.open({ name: 'late-panel', path: '', title: 'Late', panel: 'late' })

    const wrapper = mount(TDesktopHost, { global: { plugins: [router], stubs } })
    await nextTick()

    // A desktop restored from localStorage paints its windows immediately,
    // while the component that owns the surface is still coming up. With a
    // non-reactive registry the window resolved `null` once and never re-ran -
    // measured as a restored chat window stuck on "Page not found".
    expect(wrapper.find('.t-desktop-panel-probe').exists()).toBe(false)

    registerDesktopPanel('late', { name: 'LatePanel', template: '<div class="t-desktop-panel-probe" />' })
    await nextTick()

    expect(wrapper.find('.t-desktop-panel-probe').exists()).toBe(true)
  })
})

describe('TDesktopIcons selection', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('clears the highlight once the icon opens something', async () => {
    const routeStore = useAdminRouteStore()
    vi.spyOn(routeStore, 'menus', 'get').mockReturnValue([
      { key: 'dashboard', label: 'Dashboard', path: '/dashboard' },
    ])
    const wrapper = mount(TDesktopIcons, { global: { stubs } })
    const icon = wrapper.find('.t-desktop-icons__item')

    await icon.trigger('click')
    expect(icon.classes()).toContain('t-desktop-icons__item--selected')

    await icon.trigger('dblclick')
    // The window takes the focus; a highlight left on the desktop claims
    // something is still selected there.
    expect(wrapper.find('.t-desktop-icons__item').classes()).not.toContain(
      't-desktop-icons__item--selected',
    )
  })
})
