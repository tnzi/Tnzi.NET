/**
 * A click from a real pointer, on a button whose handler swaps its own icon.
 *
 * In a browser a user's click runs the page's microtasks after every listener,
 * so Vue re-renders the button (a new `<svg>` in place of the one under the
 * pointer) before an ancestor's listener sees the same click: for that
 * listener `event.target` is detached. `element.click()` runs microtasks only
 * after the whole dispatch, so a test clicking by script never sees it.
 *
 * happy-dom has no checkpoint between listeners either, so `realClick` puts
 * the page in the state a browser does: the full pointer sequence on the icon
 * node itself, and a listener that runs right after the button's own handler
 * detaches that icon node, as the re-render would. Every listener after it
 * then meets the detached target, as in Chromium (the node is replaced by a
 * copy, so the button is not left empty for the next click).
 */
import { afterEach, describe, expect, it } from 'vitest'
import { createApp, h, nextTick, ref, type App } from 'vue'

const { default: TDockedDialog } = await import('../../src/components/overlay/TDockedDialog.vue')
const { default: TCollapsibleSidebar } = await import('../../src/components/layout/TCollapsibleSidebar.vue')
const { eventPath, closestOnPath, startedWithin, trackEventPaths } = await import('../../src/headless/event-path')

let apps: App[] = []

function mount(render: () => unknown): HTMLElement {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const app = createApp({ render })
  app.mount(host)
  apps.push(app)
  return host
}

afterEach(() => {
  for (const app of apps) app.unmount()
  apps = []
  document.body.innerHTML = ''
})

const flush = async () => {
  await nextTick()
  await nextTick()
}

/**
 * A real pointer's click on `icon` (the node under the pointer), inside
 * `button`: pointerdown, mousedown, pointerup, mouseup, click, with the icon
 * detached right after the button's own click handler has run.
 */
function realClick(button: Element, icon: Element): void {
  const init = { bubbles: true, cancelable: true, composed: true, button: 0, clientX: 5, clientY: 5 }
  icon.dispatchEvent(new PointerEvent('pointerdown', { ...init, pointerType: 'mouse' }))
  icon.dispatchEvent(new MouseEvent('mousedown', init))
  icon.dispatchEvent(new PointerEvent('pointerup', { ...init, pointerType: 'mouse' }))
  icon.dispatchEvent(new MouseEvent('mouseup', init))
  /* Registered after the component's own handler on the same element, so it
     runs right after it: where the browser's microtask checkpoint re-renders. */
  const rerender = (): void => icon.replaceWith(icon.cloneNode(true))
  button.addEventListener('click', rerender, { once: true })
  icon.dispatchEvent(new MouseEvent('click', init))
}

function iconOf(button: Element): Element {
  const icon = button.querySelector('svg, .iconify, span, i')
  if (!icon) throw new Error('no icon in the button')
  return icon
}

describe('event-path', () => {
  it('keeps the path the event took even after its target left the document', () => {
    trackEventPaths()
    const outer = document.createElement('section')
    const button = document.createElement('button')
    const icon = document.createElement('i')
    button.appendChild(icon)
    outer.appendChild(button)
    document.body.appendChild(outer)
    let seen: { closest: Element | null; within: boolean; path: Element[] } | null = null
    button.addEventListener('click', () => icon.remove())
    outer.addEventListener('click', (event) => {
      seen = { closest: closestOnPath(event, 'button', outer), within: startedWithin(event, button), path: eventPath(event) }
    })
    icon.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }))
    expect(icon.isConnected).toBe(false)
    expect(seen!.closest).toBe(button)
    expect(seen!.within).toBe(true)
    expect(seen!.path.slice(0, 3)).toEqual([icon, button, outer])
  })
})

describe('a real click on a button that swaps its icon', () => {
  it('TDockedDialog: minimise stays minimised (the title row does not open it again in the same click)', async () => {
    /* `minimized` left to the dialog (no v-model): its own model changes at once, which is the
       state a browser reaches at its checkpoint after the button's handler (with a v-model the
       parent's update is flushed there too, and the header sees the folded window). */
    const states: string[] = []
    mount(() => h(TDockedDialog, { modelValue: true, title: 'New message', narrow: false, 'onUpdate:minimized': (v: boolean) => states.push(String(v)) }))
    await flush()
    const panel = () => document.querySelector<HTMLElement>('[data-docked-dialog]')!
    const fold = document.querySelector<HTMLElement>('[data-docked-minimize]')!
    realClick(fold, iconOf(fold))
    await flush()
    expect(states).toEqual(['true'])
    expect(panel().dataset.state).toBe('minimized')

    /* Restoring with the same button, and maximising, are one change each too. */
    const restore = document.querySelector<HTMLElement>('[data-docked-minimize]')!
    realClick(restore, iconOf(restore))
    await flush()
    expect(states).toEqual(['true', 'false'])
    const grow = document.querySelector<HTMLElement>('[data-docked-maximize]')!
    realClick(grow, iconOf(grow))
    await flush()
    expect(panel().dataset.state).toBe('maximized')
    expect(states).toEqual(['true', 'false'])
  })

  it('TDockedDialog: a click on the folded title row itself still opens it', async () => {
    const minimized = ref(true)
    mount(() =>
      h(TDockedDialog, {
        modelValue: true,
        minimized: minimized.value,
        'onUpdate:minimized': (v: boolean) => (minimized.value = v),
        title: 'New message',
        narrow: false,
      }),
    )
    await flush()
    document.querySelector<HTMLElement>('.t-docked-dialog__title')!.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }))
    await flush()
    expect(minimized.value).toBe(false)
  })

  it('TCollapsibleSidebar: a button in the icon rail does not also expand the sidebar', async () => {
    const mode = ref<'icon' | 'expanded'>('icon')
    const pressed = ref(false)
    mount(() =>
      h(
        TCollapsibleSidebar,
        { modelValue: mode.value, 'onUpdate:modelValue': (v: 'icon' | 'expanded') => (mode.value = v), isMobile: false },
        { rail: () => h('button', { type: 'button', class: 'pin', onClick: () => (pressed.value = !pressed.value) }, [h('i', { class: 'glyph' })]) },
      ),
    )
    await flush()
    const button = document.querySelector<HTMLElement>('.pin')!
    realClick(button, button.querySelector('.glyph')!)
    await flush()
    expect(pressed.value).toBe(true)
    expect(mode.value).toBe('icon')
  })
})
