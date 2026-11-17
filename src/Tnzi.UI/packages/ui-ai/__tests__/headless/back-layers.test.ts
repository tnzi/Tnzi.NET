import { describe, it, expect, vi } from 'vitest'
import {
  BackLayerController,
  BACK_LAYER_STATE_KEY,
  type BackLayerControllerOptions,
  type BackLayerWindow,
} from '../../src/headless/back-layers'

/**
 * A window whose history behaves like Chromium's: `pushState` drops the
 * forward entries, a traversal is asynchronous (a task later) and its target
 * is fixed when it is asked for, so a push made in between is undone by it.
 * Listeners at the window run in the order they were added, capture or not,
 * as measured in Chromium. `routerPops` records what a router's listener,
 * added after the module started listening, saw.
 */
interface Entry {
  state: unknown
  url: string
}

class FakeHistory {
  entries: Entry[]
  index = 0
  constructor(private readonly win: FakeWindow, url: string, state: unknown) {
    this.entries = [{ state, url }]
  }
  get state(): unknown {
    return this.entries[this.index]!.state
  }
  get length(): number {
    return this.entries.length
  }
  pushState(state: unknown, _unused: string, url?: string | URL | null): void {
    const next = url == null ? this.entries[this.index]!.url : new URL(String(url), this.entries[this.index]!.url).href
    this.entries = [...this.entries.slice(0, this.index + 1), { state: structuredClone(state), url: next }]
    this.index = this.entries.length - 1
  }
  replaceState(state: unknown, _unused: string, url?: string | URL | null): void {
    const next = url == null ? this.entries[this.index]!.url : new URL(String(url), this.entries[this.index]!.url).href
    this.entries = this.entries.map((entry, i) => (i === this.index ? { state: structuredClone(state), url: next } : entry))
  }
  go(delta = 0): void {
    const target = Math.max(0, Math.min(this.entries.length - 1, this.index + delta))
    setTimeout(() => {
      if (target === this.index) return
      this.index = target
      this.win.dispatchPop(this.state)
    }, 1)
  }
  back(): void {
    this.go(-1)
  }
  forward(): void {
    this.go(1)
  }
}

type Listener = (event: Event) => void

class FakeWindow implements BackLayerWindow {
  readonly history: FakeHistory
  readonly location: { readonly href: string }
  /** The Navigation API's answer, as Chromium gives it. */
  readonly navigation: { readonly canGoForward: boolean }
  private listeners = new Map<string, Listener[]>()

  constructor(url: string, state: unknown) {
    this.history = new FakeHistory(this, url, state)
    const history = this.history
    this.location = {
      get href() {
        return history.entries[history.index]!.url
      },
    }
    this.navigation = {
      get canGoForward() {
        return history.index < history.entries.length - 1
      },
    }
  }

  get historyApi(): History {
    return this.history as unknown as History
  }

  addEventListener(type: string, listener: Listener): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener])
  }
  removeEventListener(type: string, listener: Listener): void {
    this.listeners.set(type, (this.listeners.get(type) ?? []).filter((l) => l !== listener))
  }
  setTimeout(handler: () => void, timeout?: number): number {
    return setTimeout(handler, timeout) as unknown as number
  }
  clearTimeout(id: number | undefined): void {
    clearTimeout(id)
  }

  dispatch(type: string, event: Event): void {
    let stopped = false
    const original = event.stopImmediatePropagation.bind(event)
    event.stopImmediatePropagation = () => {
      stopped = true
      original()
    }
    for (const listener of [...(this.listeners.get(type) ?? [])]) {
      if (stopped) return
      listener(event)
    }
  }
  dispatchPop(state: unknown): void {
    const event = new Event('popstate') as PopStateEvent
    Object.defineProperty(event, 'state', { value: state })
    this.dispatch('popstate', event)
  }
  tap(): void {
    this.dispatch('pointerdown', new Event('pointerdown'))
    this.dispatch('pointerup', new Event('pointerup'))
  }
}

/** A router-shaped page: its listener records the pops it is given, and it pushes with its state. */
function setup(url = 'https://app.test/recents', options: BackLayerControllerOptions = {}) {
  const win = new FakeWindow('https://app.test/', { position: 0, current: '/' })
  win.history.pushState({ position: 1, current: '/recents' }, '', url)
  // Started before the router, as `defineChatApp` / `installBackLayers` do.
  const controller = new BackLayerController(win as unknown as BackLayerWindow, options)
  controller.listen()
  const routerPops: string[] = []
  win.addEventListener('popstate', () => routerPops.push(win.location.href))
  const routerPush = (path: string, position: number) => win.historyApi.pushState({ position, current: path }, '', path)
  return { win, controller, routerPops, routerPush }
}

/** Lets the timers and promises of a step run out. */
async function settle(): Promise<void> {
  for (let i = 0; i < 6; i++) await new Promise((resolve) => setTimeout(resolve, 12))
}

function userBack(win: FakeWindow): Promise<void> {
  win.historyApi.back()
  return settle()
}

/** Longer than the module waits for the history to be still before clearing forward history. */
async function quiet(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 400))
  await settle()
}

/** `history.state` without the module's key. */
function routerState(win: FakeWindow): Record<string, unknown> {
  const { [BACK_LAYER_STATE_KEY]: _marker, ...rest } = win.history.state as Record<string, unknown>
  return rest
}

const PHONE: BackLayerControllerOptions = { trimForward: () => true, forwardSwipe: () => false }
const IPHONE: BackLayerControllerOptions = { trimForward: () => true, forwardSwipe: () => true }

describe('BackLayerController', () => {
  it('gives an open layer an entry with the same address and the router state kept', async () => {
    const { win, controller } = setup()
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    expect(win.history.length).toBe(3)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(win.history.state).toMatchObject({ position: 1, current: '/recents' })
    expect((win.history.state as Record<string, unknown>)[BACK_LAYER_STATE_KEY]).toBeTruthy()
  })

  it('closes the layer on back, without the router seeing it', async () => {
    const { win, controller, routerPops } = setup()
    win.tap()
    const onBack = vi.fn(() => layer.close())
    const layer = controller.createLayer(onBack)
    layer.open()
    await settle()
    await userBack(win)
    expect(onBack).toHaveBeenCalledTimes(1)
    expect(layer.isOpen).toBe(false)
    expect(routerPops).toEqual([])
    expect(win.location.href).toBe('https://app.test/recents')
    expect(win.history.index).toBe(1)
  })

  it('takes the entry out when the layer closes another way, so one back reaches the previous page', async () => {
    const { win, controller, routerPops } = setup()
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    layer.close()
    await settle()
    expect(win.history.index).toBe(1)
    expect(routerPops).toEqual([])
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/')
    expect(routerPops).toEqual(['https://app.test/'])
  })

  it('holds a navigation made while the entry is being taken out, so the new page sits on the old one', async () => {
    const { win, controller, routerPush, routerPops } = setup()
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    layer.close()
    // A router push a few microtasks later, as from a nav link in the drawer.
    await Promise.resolve()
    await Promise.resolve()
    routerPush('/notes', 2)
    await settle()
    expect(win.location.href).toBe('https://app.test/notes')
    expect(win.history.entries.map((e) => e.url)).toEqual([
      'https://app.test/',
      'https://app.test/recents',
      'https://app.test/notes',
    ])
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual(['https://app.test/recents'])
  })

  it('closes one layer per back, the top one first', async () => {
    const { win, controller } = setup()
    const closed: string[] = []
    win.tap()
    const drawer = controller.createLayer(() => {
      closed.push('drawer')
      drawer.close()
    })
    drawer.open()
    await settle()
    win.tap()
    const menu = controller.createLayer(() => {
      closed.push('menu')
      menu.close()
    })
    menu.open()
    await settle()
    expect(win.history.length).toBe(4)
    await userBack(win)
    expect(closed).toEqual(['menu'])
    expect(drawer.isOpen).toBe(true)
    await userBack(win)
    expect(closed).toEqual(['menu', 'drawer'])
    expect(win.location.href).toBe('https://app.test/recents')
    expect(win.history.index).toBe(1)
  })

  it('keeps a layer that refuses to close, and the page with it', async () => {
    const { win, controller, routerPops } = setup()
    win.tap()
    const layer = controller.createLayer(() => false)
    layer.open()
    await settle()
    await userBack(win)
    await settle()
    expect(layer.isOpen).toBe(true)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual([])
    // It has its entry again: the next back is absorbed too.
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual([])
  })

  it('waits for an asynchronous answer', async () => {
    const { win, controller } = setup()
    win.tap()
    let answer!: (value: boolean) => void
    const layer = controller.createLayer(() => new Promise<boolean>((resolve) => (answer = resolve)))
    layer.open()
    await settle()
    await userBack(win)
    answer(false)
    await settle()
    expect(layer.isOpen).toBe(true)
    expect((win.history.state as Record<string, unknown>)[BACK_LAYER_STATE_KEY]).toBeTruthy()
  })

  it('re-arms a layer whose owner kept it open after accepting', async () => {
    const { win, controller } = setup()
    win.tap()
    const layer = controller.createLayer(() => true)
    layer.open()
    await settle()
    await userBack(win)
    await settle()
    expect((win.history.state as Record<string, unknown>)[BACK_LAYER_STATE_KEY]).toBeTruthy()
  })

  it('takes no entry for a layer that came with an address change', async () => {
    const { win, controller, routerPush } = setup()
    const layer = controller.createLayer(() => {})
    win.tap()
    routerPush('/recents?settings=all', 2)
    layer.open()
    await settle()
    expect(win.history.length).toBe(3)
    expect(controller.debugState.entries).toBe(0)
  })

  it('takes no entry for a layer opened by a navigation before any input', async () => {
    const { controller, routerPush } = setup()
    const layer = controller.createLayer(() => {})
    routerPush('/recents?settings=all', 2)
    layer.open()
    await settle()
    expect(controller.debugState.entries).toBe(0)
  })

  it('gives an entry to a layer open on the address the page started with', async () => {
    const { win, controller } = setup()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    expect(controller.debugState.entries).toBe(1)
    expect(win.history.length).toBe(3)
  })

  it('keeps the marker on a replace the router makes on the layer entry', async () => {
    const { win, controller } = setup()
    win.tap()
    const onBack = vi.fn()
    controller.createLayer(onBack).open()
    await settle()
    win.historyApi.replaceState({ position: 1, current: '/recents', scroll: { top: 0 } }, '', 'https://app.test/recents')
    expect((win.history.state as Record<string, unknown>)[BACK_LAYER_STATE_KEY]).toBeTruthy()
    await userBack(win)
    expect(onBack).toHaveBeenCalledTimes(1)
  })

  it('steps over an entry left behind by a navigation made while a layer was open', async () => {
    const { win, controller, routerPush, routerPops } = setup()
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    // Navigation without closing first, then the layer closes on the new page.
    routerPush('/notes', 2)
    layer.close()
    await settle()
    expect(win.history.entries.map((e) => e.url)).toEqual([
      'https://app.test/',
      'https://app.test/recents',
      'https://app.test/recents',
      'https://app.test/notes',
    ])
    await userBack(win)
    await settle()
    expect(win.location.href).toBe('https://app.test/recents')
    expect(win.history.index).toBe(1)
    expect(routerPops).toEqual(['https://app.test/recents'])
  })

  it('keeps a layer left open under a page pushed over it: back returns to the page, then closes the layer', async () => {
    const { win, controller, routerPush, routerPops } = setup()
    win.tap()
    const onBack = vi.fn(() => layer.close())
    const layer = controller.createLayer(onBack)
    layer.open()
    await settle()
    routerPush('/recents?settings=all', 2)
    await settle()
    expect(win.history.length).toBe(4)
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual(['https://app.test/recents'])
    expect(onBack).not.toHaveBeenCalled()
    await userBack(win)
    expect(onBack).toHaveBeenCalledTimes(1)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual(['https://app.test/recents'])
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/')
  })

  it('closes a menu and lets the page it opens go over the layer under it, in that order on back', async () => {
    const { win, controller, routerPush, routerPops } = setup()
    win.tap()
    const drawer = controller.createLayer(() => drawer.close())
    drawer.open()
    await settle()
    win.tap()
    const menu = controller.createLayer(() => menu.close())
    menu.open()
    await settle()
    // A menu item that navigates: the menu closes, the router pushes.
    menu.close()
    await Promise.resolve()
    routerPush('/recents?settings=all', 2)
    await settle()
    expect(win.history.entries.map((e) => e.url)).toEqual([
      'https://app.test/',
      'https://app.test/recents',
      'https://app.test/recents',
      'https://app.test/recents?settings=all',
    ])
    await userBack(win) // the page over the drawer goes first
    expect(routerPops).toEqual(['https://app.test/recents'])
    expect(drawer.isOpen).toBe(true)
    await userBack(win) // then the drawer
    expect(drawer.isOpen).toBe(false)
    expect(win.history.index).toBe(1)
  })

  it('preserves the router state on every entry, adding only its own key', async () => {
    const { win, controller, routerPush } = setup()
    win.tap()
    controller.createLayer(() => {}).open()
    await settle()
    routerPush('/notes', 2)
    for (const entry of win.history.entries.slice(1)) {
      const { [BACK_LAYER_STATE_KEY]: marker, ...rest } = entry.state as Record<string, unknown>
      expect(marker).toBeTruthy()
      expect(Object.keys(rest)).toEqual(expect.arrayContaining(['position', 'current']))
    }
  })

  it('reports when a traversal of its own has landed', async () => {
    const { win, controller } = setup()
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    layer.close()
    await Promise.resolve()
    expect(controller.debugState.traversing).toBe(true)
    await controller.whenSettled()
    expect(controller.debugState.traversing).toBe(false)
    expect(win.history.index).toBe(1)
  })

  it('does not reopen a disposed layer', async () => {
    const { win, controller } = setup()
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.dispose()
    layer.open()
    await settle()
    expect(layer.isOpen).toBe(false)
    expect(win.history.length).toBe(2)
  })
})

describe('BackLayerController on a narrow screen: no forward history', () => {
  /** `/` (outside the model), `/recents` (where the model starts), then the router's pages. */
  function phone(options: BackLayerControllerOptions = PHONE) {
    const env = setup('https://app.test/recents', options)
    const pages = env.controller.createLayer(() => {}) // as the shell's drawer: created with the page
    return { ...env, pages }
  }

  it('closing a layer another way leaves nothing forward, and one back still reaches the previous page', async () => {
    const { win, controller, routerPush, routerPops } = phone()
    routerPush('/notes', 2)
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    const before = routerState(win)
    layer.close()
    await settle()
    expect(win.navigation.canGoForward).toBe(false)
    expect(win.location.href).toBe('https://app.test/notes')
    expect(routerState(win)).toEqual(before)
    expect(routerPops).toEqual([])
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual(['https://app.test/recents'])
  })

  it('closing a layer by back leaves nothing forward once the history is still', async () => {
    const { win, controller, routerPush, routerPops } = phone()
    routerPush('/notes', 2)
    win.tap()
    const layer = controller.createLayer(() => layer.close())
    layer.open()
    await settle()
    await userBack(win)
    expect(layer.isOpen).toBe(false)
    await quiet()
    expect(win.navigation.canGoForward).toBe(false)
    expect(win.location.href).toBe('https://app.test/notes')
    expect(routerPops).toEqual([])
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
  })

  it('going back between pages leaves nothing forward, without the router hearing more than the back', async () => {
    const { win, routerPush, routerPops } = phone()
    routerPush('/chat/1', 2)
    routerPush('/chat/2', 3)
    await userBack(win)
    const state = routerState(win)
    expect(win.navigation.canGoForward).toBe(true)
    await quiet()
    expect(win.navigation.canGoForward).toBe(false)
    expect(win.location.href).toBe('https://app.test/chat/1')
    expect(routerState(win)).toEqual(state)
    expect(routerPops).toEqual(['https://app.test/chat/1'])
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
  })

  it('a page opened from the drawer sits on the page the drawer was opened on, nothing forward', async () => {
    const { win, controller, routerPush, routerPops } = phone()
    win.tap()
    const drawer = controller.createLayer(() => drawer.close())
    drawer.open()
    await settle()
    drawer.close()
    await Promise.resolve()
    routerPush('/notes', 2)
    await settle()
    expect(win.history.entries.slice(win.history.index - 1).map((e) => e.url)).toEqual([
      'https://app.test/recents',
      'https://app.test/notes',
    ])
    expect(win.navigation.canGoForward).toBe(false)
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual(['https://app.test/recents'])
  })

  it('a dialog that cannot close stays, and the page with it', async () => {
    const { win, controller, routerPush, routerPops } = phone()
    routerPush('/notes', 2)
    win.tap()
    const layer = controller.createLayer(() => false)
    layer.open()
    await settle()
    await userBack(win)
    await quiet()
    expect(layer.isOpen).toBe(true)
    expect(win.location.href).toBe('https://app.test/notes')
    expect(routerPops).toEqual([])
    expect(win.navigation.canGoForward).toBe(false)
  })

  it('a layer that came with the address closes in one back', async () => {
    const { win, controller, routerPush, routerPops } = phone()
    routerPush('/notes', 2)
    const layer = controller.createLayer(() => {})
    win.tap()
    routerPush('/notes?settings=all', 3)
    layer.open()
    await settle()
    await userBack(win)
    expect(routerPops).toEqual(['https://app.test/notes'])
    await quiet()
    expect(win.location.href).toBe('https://app.test/notes')
    expect(win.navigation.canGoForward).toBe(false)
  })

  it('waits while a finger is down', async () => {
    const { win, routerPush } = phone()
    routerPush('/notes', 2)
    routerPush('/a', 3)
    await userBack(win)
    win.dispatch('pointerdown', new Event('pointerdown'))
    await quiet()
    expect(win.navigation.canGoForward).toBe(true)
    win.dispatch('pointercancel', new Event('pointercancel'))
    await quiet()
    expect(win.navigation.canGoForward).toBe(false)
  })

  it('loses no step in a quick series of backs', async () => {
    const { win, routerPush, routerPops } = phone()
    routerPush('/a', 2)
    routerPush('/b', 3)
    routerPush('/c', 4)
    win.historyApi.back()
    await settle()
    win.historyApi.back()
    await settle()
    expect(win.location.href).toBe('https://app.test/a')
    await quiet()
    expect(win.location.href).toBe('https://app.test/a')
    expect(routerPops).toEqual(['https://app.test/b', 'https://app.test/a'])
    expect(win.navigation.canGoForward).toBe(false)
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
  })

  it('never steps to an entry outside the model: on its first entry the forward entry stays where there is no forward swipe', async () => {
    const { win, controller, routerPops } = phone()
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    layer.close()
    await quiet()
    // `/` is not the model's: the module never goes there.
    expect(win.history.index).toBe(1)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual([])
    expect(win.navigation.canGoForward).toBe(true)
    // Forward lands on the stale entry: it is kept, not bounced back from.
    win.historyApi.forward()
    await quiet()
    expect(win.history.index).toBe(2)
    expect(win.navigation.canGoForward).toBe(false)
    expect(routerPops).toEqual([])
  })

  it('with a forward swipe, the first entry is pushed again on top of itself, once', async () => {
    const { win, routerPush, routerPops } = phone(IPHONE)
    routerPush('/notes', 2)
    await userBack(win)
    await quiet()
    expect(win.navigation.canGoForward).toBe(false)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual(['https://app.test/recents'])
    // Back from the repeat: the same page, nobody told; then nothing more is pushed.
    await userBack(win)
    await quiet()
    expect(win.history.index).toBe(1)
    expect(routerPops).toEqual(['https://app.test/recents'])
    expect(win.navigation.canGoForward).toBe(true)
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/')
  })

  it('with a forward swipe, closing a layer on the first entry leaves nothing forward', async () => {
    const { win, controller, routerPops } = phone(IPHONE)
    win.tap()
    const layer = controller.createLayer(() => {})
    layer.open()
    await settle()
    layer.close()
    await settle()
    expect(win.navigation.canGoForward).toBe(false)
    expect(win.location.href).toBe('https://app.test/recents')
    expect(routerPops).toEqual([])
  })

  it('keeps a layer left open under a page pushed over it', async () => {
    const { win, controller, routerPush, routerPops } = phone()
    routerPush('/notes', 2)
    win.tap()
    const onBack = vi.fn(() => layer.close())
    const layer = controller.createLayer(onBack)
    layer.open()
    await settle()
    routerPush('/notes?settings=all', 3)
    await userBack(win)
    await quiet()
    expect(win.navigation.canGoForward).toBe(false)
    expect(onBack).not.toHaveBeenCalled()
    expect(layer.isOpen).toBe(true)
    await userBack(win)
    expect(onBack).toHaveBeenCalledTimes(1)
    expect(win.location.href).toBe('https://app.test/notes')
    expect(routerPops).toEqual(['https://app.test/notes'])
    await quiet()
    expect(win.navigation.canGoForward).toBe(false)
    await userBack(win)
    expect(win.location.href).toBe('https://app.test/recents')
  })

  /**
   * A router whose guards take `delay` ms, then pushes as vue-router does:
   * reads `history.state` at that moment, replaces the current entry with it
   * (adding `forward`), then pushes the new page.
   */
  function slowRouterPush(win: FakeWindow, path: string, delay: number): Promise<void> {
    return new Promise((resolve) =>
      setTimeout(() => {
        const current = { ...(win.history.state as Record<string, unknown>) }
        win.historyApi.replaceState({ ...current, forward: path }, '', String(current.current))
        win.historyApi.pushState({ current: path, back: current.current, position: Number(current.position) + 1 }, '', path)
        resolve()
      }, delay),
    )
  }

  function entryPaths(win: FakeWindow): string[] {
    return win.history.entries.map((e) => new URL(e.url).pathname)
  }

  function entryCurrents(win: FakeWindow): unknown[] {
    return win.history.entries.map((e) => (e.state as Record<string, unknown> | null)?.current)
  }

  for (const delay of [0, 1, 2, 5, 10, 30, 60, 100, 300]) {
    for (const [label, options] of [['no forward swipe', PHONE], ['forward swipe', IPHONE]] as const) {
      it(`a page opened from the drawer with guards taking ${delay}ms keeps every entry right (${label})`, async () => {
        const win = new FakeWindow('https://app.test/', { position: 0, current: '/' })
        win.history.pushState({ position: 1, current: '/reminders' }, '', 'https://app.test/reminders')
        const controller = new BackLayerController(win as unknown as BackLayerWindow, options)
        controller.listen()
        const routerPops: string[] = []
        win.addEventListener('popstate', () => routerPops.push(win.location.href))
        const drawer = controller.createLayer(() => drawer.close())
        for (const path of ['/notes', '/schedules']) {
          win.tap()
          drawer.open()
          await settle()
          drawer.close()
          await slowRouterPush(win, path, delay)
          await quiet()
        }
        const last = win.history.index
        expect(entryPaths(win).slice(last - 2, last + 1)).toEqual(['/reminders', '/notes', '/schedules'])
        expect(entryCurrents(win).slice(last - 2, last + 1)).toEqual(['/reminders', '/notes', '/schedules'])
        expect(win.navigation.canGoForward).toBe(false)
        await userBack(win)
        expect(win.location.href).toBe('https://app.test/notes')
        expect((win.history.state as Record<string, unknown>).current).toBe('/notes')
        await quiet()
        await userBack(win)
        expect(win.location.href).toBe('https://app.test/reminders')
        expect((win.history.state as Record<string, unknown>).current).toBe('/reminders')
        expect(routerPops).toEqual(['https://app.test/notes', 'https://app.test/reminders'])
      })
    }
  }
})
