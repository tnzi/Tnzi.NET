import { describe, it, expect, beforeEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import TDesktopHost from '../../../src/components/desktop/TDesktopHost.vue'
import TDesktopWindowNav from '../../../src/components/desktop/TDesktopWindowNav.vue'
import { useAdminDesktopStore } from '../../../src/stores/useAdminDesktopStore'
import { useAdminRouteStore, type AdminMenuItem } from '../../../src/stores/useAdminRouteStore'
import {
  MIN_VISIBLE,
  WINDOW_SIZE_PRESETS,
  isPositionReachable,
} from '../../../src/headless/window-sizing'

const Blank = defineComponent({ render: () => h('div', { class: 'page-body' }) })

const stubs = {
  NResult: { template: '<div class="n-result" />' },
  NSpin: { template: '<div class="n-spin" />' },
  NDropdown: { template: '<div class="n-dropdown" />' },
  NTooltip: { template: '<div><slot name="trigger" /></div>' },
  NPopover: { template: '<div><slot name="trigger" /><slot /></div>' },
  NInput: { template: '<input class="n-input" />' },
  NScrollbar: { template: '<div><slot /></div>' },
  NMenu: {
    props: ['options', 'value', 'collapsed'],
    template: '<div class="n-menu" :data-value="value" :data-collapsed="String(collapsed)" />',
  },
  TSvgIcon: { props: ['icon'], template: '<i class="t-icon" :data-icon="icon" />' },
  TAvatar: { template: '<div class="t-avatar" />' },
  TDesktopWindowHost: { template: '<div class="window-host" />' },
}

/**
 * A consumer-shaped module: hyphenated route names sharing no dotted prefix,
 * which is how a real consuming app names its own pages. Fed through
 * `setAuthRoutes` rather than by stubbing `menus`, because `menuModuleByRoute`
 * is derived from the store's INTERNAL menu computed - a spy on the exposed
 * getter is invisible to it, and every module lookup would silently fall back
 * to the dotted-name rule.
 */
const ROUTES = [
  {
    name: 'shop',
    path: '/shop',
    meta: { title: 'Shop', icon: 'mdi:store' },
    children: [
      { name: 'shop-orders', path: 'orders', meta: { title: 'Orders' } },
      { name: 'shop-products', path: 'products', meta: { title: 'Products' } },
    ],
  },
  { name: 'dashboard', path: '/dashboard', meta: { title: 'Dashboard' } },
]

/** The menu tree the store built, so tests hand real items to the handlers. */
function menus(): AdminMenuItem[] {
  return useAdminRouteStore().menus
}

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: Blank },
      { path: '/shop/orders', name: 'shop-orders', component: Blank, meta: { title: 'Orders' } },
      { path: '/shop/products', name: 'shop-products', component: Blank, meta: { title: 'Products' } },
      { path: '/dashboard', name: 'dashboard', component: Blank, meta: { title: 'Dashboard' } },
    ],
  })
}

describe('desktop module windows', () => {
  let router: Router

  beforeEach(async () => {
    setActivePinia(createPinia())
    router = makeRouter()
    await router.push('/')
    await router.isReady()
    useAdminRouteStore().setAuthRoutes(ROUTES)
  })

  function mountHost() {
    return mount(TDesktopHost, { global: { plugins: [router], stubs } })
  }

  it('opens a module tile onto its first page', async () => {
    const host = mountHost()
    const desktop = useAdminDesktopStore()

    host.findComponent({ name: 'TDesktopIcons' }).vm.$emit('open', menus()[0])
    await nextTick()

    expect(desktop.windows).toHaveLength(1)
    expect(desktop.windows[0].stack[0].name).toBe('shop-orders')
  })

  it('brings the module forward instead of opening a second window', async () => {
    const host = mountHost()
    const desktop = useAdminDesktopStore()
    const icons = host.findComponent({ name: 'TDesktopIcons' })

    icons.vm.$emit('open', menus()[0])
    await nextTick()
    desktop.navigate(desktop.windows[0].id, {
      name: 'shop-products',
      path: '/shop/products',
      title: 'Products',
    })

    icons.vm.$emit('open', menus()[0])
    await nextTick()

    // One window, and it did NOT get thrown back to page one - the user asked
    // for the module, not for its first page.
    expect(desktop.windows).toHaveLength(1)
    const win = desktop.windows[0]
    expect(win.stack[win.stackIndex].name).toBe('shop-products')
    expect(win.stack).toHaveLength(2)
  })

  it('navigates the module window when a specific page is asked for', async () => {
    const host = mountHost()
    const desktop = useAdminDesktopStore()

    host.findComponent({ name: 'TDesktopIcons' }).vm.$emit('open', menus()[0])
    await nextTick()

    // The start menu hands over a leaf, not the module.
    host.findComponent({ name: 'TDesktopTaskbar' }).vm.$emit('open', menus()[0].children![1])
    await nextTick()

    expect(desktop.windows).toHaveLength(1)
    const win = desktop.windows[0]
    expect(win.stack[win.stackIndex].name).toBe('shop-products')
  })

  it('does not push a duplicate entry when the page is already showing', async () => {
    const host = mountHost()
    const desktop = useAdminDesktopStore()

    host.findComponent({ name: 'TDesktopIcons' }).vm.$emit('open', menus()[1])
    await nextTick()
    host.findComponent({ name: 'TDesktopIcons' }).vm.$emit('open', menus()[1])
    await nextTick()

    // A duplicate entry would leave the back button looking broken: pressed
    // once, nothing visibly changes.
    expect(desktop.windows[0].stack).toHaveLength(1)
  })

  it('renders the module nav beside the page, keyed to the open page', async () => {
    const host = mountHost()
    host.findComponent({ name: 'TDesktopIcons' }).vm.$emit('open', menus()[0])
    await nextTick()

    const nav = host.findComponent(TDesktopWindowNav)
    expect(nav.exists()).toBe(true)
    expect(nav.props('moduleLabel')).toBeTruthy()
    expect(nav.props('items').map((i: AdminMenuItem) => i.key)).toEqual([
      'shop-orders',
      'shop-products',
    ])
    expect(nav.props('activeKey')).toBe('shop-orders')
  })

  it('renders no nav for a module with a single page', async () => {
    const host = mountHost()
    host.findComponent({ name: 'TDesktopIcons' }).vm.$emit('open', menus()[1])
    await nextTick()

    // Nothing to navigate between; the nav would be 190px of dead width.
    expect(host.findComponent(TDesktopWindowNav).exists()).toBe(false)
  })

  it('navigates inside the window when the nav is used', async () => {
    const host = mountHost()
    const desktop = useAdminDesktopStore()
    host.findComponent({ name: 'TDesktopIcons' }).vm.$emit('open', menus()[0])
    await nextTick()

    host.findComponent(TDesktopWindowNav).vm.$emit('select', menus()[0].children![1])
    await nextTick()

    // Same window, and the stack grew so the window's own back button works.
    expect(desktop.windows).toHaveLength(1)
    const win = desktop.windows[0]
    expect(win.stack.map((l) => l.name)).toEqual(['shop-orders', 'shop-products'])
    expect(win.stackIndex).toBe(1)
  })

  it('opens a route as a window for chrome that would otherwise router.push', () => {
    const desktop = useAdminDesktopStore()

    // What `goUserCenter` and a global-search hit do in desktop mode. A push
    // there navigates a router whose outlet the window manager replaced: the
    // address bar moves and nothing else does.
    const first = desktop.openOrFocusRoute({
      name: 'shop-orders',
      path: '/shop/orders',
      title: 'Orders',
    })
    expect(first.minted).toBe(true)
    expect(desktop.windows).toHaveLength(1)

    // Second hit inside the same module reuses the window and navigates it.
    const second = desktop.openOrFocusRoute({
      name: 'shop-products',
      path: '/shop/products',
      title: 'Products',
    })
    expect(second.minted).toBe(false)
    expect(second.id).toBe(first.id)
    expect(desktop.windows).toHaveLength(1)
    const win = desktop.windows[0]
    expect(win.stack[win.stackIndex].name).toBe('shop-products')
  })

  it('focuses without navigating when asked for the module', () => {
    const desktop = useAdminDesktopStore()
    desktop.openOrFocusRoute({ name: 'shop-products', path: '/shop/products', title: 'Products' })

    desktop.openOrFocusRoute(
      { name: 'shop-orders', path: '/shop/orders', title: 'Orders' },
      { focusOnly: true },
    )

    const win = desktop.windows[0]
    expect(desktop.windows).toHaveLength(1)
    expect(win.stack[win.stackIndex].name).toBe('shop-products')
  })

  it('opens a page at the size its route asked for', () => {
    const desktop = useAdminDesktopStore()
    desktop.setSurface({ width: 1920, height: 1080 })

    desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })
    desktop.open({
      name: 'settings-page',
      path: '/settings',
      title: 'Settings',
      size: { preset: 'compact' },
    })

    // Not one size for everything: a register and a settings form want
    // different windows.
    expect(desktop.windows[0].width).toBe(WINDOW_SIZE_PRESETS.wide.width)
    expect(desktop.windows[1].width).toBe(WINDOW_SIZE_PRESETS.compact.width)
  })

  it('reopens a module at the size the user last left it', () => {
    const desktop = useAdminDesktopStore()
    desktop.setSurface({ width: 1920, height: 1080 })

    const id = desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })
    // Widened by hand to uncover a cut-off column, then remembered on mouse-up.
    desktop.setGeometry(id, { x: 40, y: 60, width: 1700, height: 900 })
    desktop.rememberGeometry(id)
    desktop.close(id)

    // A DIFFERENT page of the same module: the window serves the module, so the
    // size follows the module, not whichever page it was closed on.
    desktop.open({ name: 'shop-products', path: '/shop/products', title: 'Products' })
    expect(desktop.windows[0].width).toBe(1700)
    expect(desktop.windows[0].x).toBe(40)
  })

  it('discards a remembered size that no longer fits the screen', () => {
    const desktop = useAdminDesktopStore()
    desktop.setSurface({ width: 1920, height: 1080 })
    const id = desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })
    desktop.setGeometry(id, { x: 0, y: 0, width: 1800, height: 1000 })
    desktop.rememberGeometry(id)
    desktop.close(id)

    // Same account, smaller screen. 1800px is not this user's preference here.
    desktop.setSurface({ width: 1366, height: 768 })
    desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })

    expect(desktop.windows[0].width).toBe(WINDOW_SIZE_PRESETS.wide.width)
    expect(desktop.windows[0].width).toBeLessThanOrEqual(1366)
  })

  it('does not learn a size from maximising', () => {
    const desktop = useAdminDesktopStore()
    const surface = { width: 1920, height: 1080 }
    desktop.setSurface(surface)
    const id = desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })

    desktop.toggleMaximize(id, surface)
    desktop.rememberGeometry(id)

    // Maximised geometry is the surface, not a choice - remembering it would
    // make every later open full-screen.
    expect(desktop.geometryMemory.shop).toBeUndefined()
  })

  it('keeps a window in the same relative place when the browser resizes', () => {
    const desktop = useAdminDesktopStore()
    desktop.setSurface({ width: 1600, height: 900 })
    const id = desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })
    // Parked against the right edge.
    desktop.setGeometry(id, { x: 1600 - 600, y: 40, width: 600, height: 400 })

    desktop.reflowToSurface({ width: 1200, height: 700 })

    // Still against the right edge - the layout follows the browser rather than
    // holding a pixel offset that means something else at the new size.
    expect(desktop.find(id)!.x).toBe(1200 - 600)
    expect(desktop.find(id)!.width).toBe(600)
  })

  it('rescues a window stranded by a shrinking viewport', () => {
    const desktop = useAdminDesktopStore()
    desktop.setSurface({ width: 1920, height: 1080 })
    const id = desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })
    // Wider than the surface it is about to find itself on - otherwise
    // "moved, not shrunk" below is vacuous, since nothing would need shrinking.
    desktop.setGeometry(id, { x: 1700, y: 900, width: 1200, height: 700 })

    // The browser gets dragged narrower. The old code only tracked maximised
    // windows after the first measurement, so this one was left off the edge
    // with nothing to grab.
    const small = { width: 1000, height: 600 }
    desktop.reflowToSurface(small)

    const win = desktop.find(id)!
    expect(isPositionReachable(win, small)).toBe(true)
    expect(win.x).toBeLessThanOrEqual(small.width - MIN_VISIBLE)
    // Moved, not shrunk: dragging a browser narrower must not ratchet windows
    // smaller and never give the size back. A window merely hanging off the
    // right edge is still perfectly usable.
    expect(win.width).toBe(1200)
    expect(win.height).toBe(700)
  })

  it('resetLayout forgets remembered geometry and lays everything out fresh', () => {
    const desktop = useAdminDesktopStore()
    const surface = { width: 1440, height: 900 }
    desktop.setSurface(surface)
    const id = desktop.open({ name: 'shop-orders', path: '/shop/orders', title: 'Orders' })
    desktop.setGeometry(id, { x: 1300, y: 800, width: 400, height: 300 })
    desktop.rememberGeometry(id)

    desktop.resetLayout()

    const win = desktop.find(id)!
    expect(desktop.geometryMemory.shop).toBeUndefined()
    expect(win.width).toBe(WINDOW_SIZE_PRESETS.wide.width)
    expect(isPositionReachable(win, surface)).toBe(true)
  })

  it('collapses the nav in a narrow window, and lets the user override that', async () => {
    const host = mountHost()
    const desktop = useAdminDesktopStore()
    host.findComponent({ name: 'TDesktopIcons' }).vm.$emit('open', menus()[0])
    await nextTick()

    desktop.setGeometry(desktop.windows[0].id, { width: 500 })
    await nextTick()
    expect(host.findComponent(TDesktopWindowNav).props('collapsed')).toBe(true)

    // An explicit instruction beats the width heuristic - otherwise the toggle
    // is a button that visibly does nothing.
    host.findComponent(TDesktopWindowNav).vm.$emit('update:collapsed', false)
    await nextTick()
    expect(host.findComponent(TDesktopWindowNav).props('collapsed')).toBe(false)
  })
})
