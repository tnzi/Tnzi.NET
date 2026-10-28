/**
 * The keyboard side of the overlay stack. The ordering itself (which layer is
 * on top as layers open and close) is `@tnzi/core`'s and is tested there
 * (`core/__tests__/headless/overlay-layers.test.ts`).
 */
import { describe, it, expect, afterEach, vi } from 'vitest'
import { effectScope, nextTick, ref, type EffectScope } from 'vue'
import { useOverlayLayer } from '../../../src/headless/feedback/useOverlayLayer'
import { useFocusTrap } from '../../../src/headless/feedback/useFocusTrap'

const scopes: EffectScope[] = []

/** A layer that is open while `open` is true, in its own scope (a component). */
function layer(options: Parameters<typeof useOverlayLayer>[1] = {}, initiallyOpen = true) {
  const open = ref(initiallyOpen)
  const scope = effectScope()
  scopes.push(scope)
  const handle = scope.run(() => useOverlayLayer(() => open.value, options))!
  return { open, scope, ...handle }
}

function press(key: string, init: KeyboardEventInit = {}, target: EventTarget = document): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init })
  target.dispatchEvent(event)
  return event
}

afterEach(() => {
  scopes.splice(0).forEach((scope) => scope.stop())
  document.body.innerHTML = ''
})

describe('useOverlayLayer', () => {
  it('hands Escape to the topmost open layer only', () => {
    const below = vi.fn()
    const above = vi.fn()
    layer({ onEscape: below })
    layer({ onEscape: above })

    press('Escape')
    expect(above).toHaveBeenCalledTimes(1)
    expect(below).not.toHaveBeenCalled()
  })

  // The top layer closing inside its own handler must not hand the same
  // Escape to the layer under it: one key press, one layer. The top layer
  // leaves the stack synchronously here (its scope ends in the handler), which
  // is what a real key press amounts to: the browser runs Vue's flush between
  // two listeners of the same event, so a layer closed by the first listener is
  // already gone when the second one asks who is on top.
  it('does not pass one Escape on when the top layer closes in its handler', () => {
    const below = vi.fn()
    layer({ onEscape: below })
    const top: ReturnType<typeof layer> = layer({ onEscape: () => top.scope.stop() })

    press('Escape')
    expect(below).not.toHaveBeenCalled()

    press('Escape')
    expect(below).toHaveBeenCalledTimes(1)
  })

  it('reports which layer is on top, and moves it as layers open and close', async () => {
    const first = layer()
    const second = layer()
    expect(first.isTop.value).toBe(false)
    expect(second.isTop.value).toBe(true)

    second.open.value = false
    await nextTick()
    expect(first.isTop.value).toBe(true)
    expect(second.isTop.value).toBe(false)
  })

  it('a layer below closing leaves the top one on top', async () => {
    const below = vi.fn()
    const above = vi.fn()
    const lower = layer({ onEscape: below })
    layer({ onEscape: above })
    lower.open.value = false
    await nextTick()

    press('Escape')
    expect(above).toHaveBeenCalledTimes(1)
    expect(below).not.toHaveBeenCalled()
  })

  it('leaves the stack when its scope ends (the component unmounts)', () => {
    const below = vi.fn()
    layer({ onEscape: below })
    const top = layer({ onEscape: vi.fn() })
    top.scope.stop()

    press('Escape')
    expect(below).toHaveBeenCalledTimes(1)
  })

  it('hands other keys to the top layer through onKeydown', () => {
    const below = vi.fn()
    const above = vi.fn()
    layer({ onKeydown: below })
    layer({ onKeydown: above })
    press('Tab')
    expect(above).toHaveBeenCalledWith(expect.objectContaining({ key: 'Tab' }))
    expect(below).not.toHaveBeenCalled()
  })

  it('ignores the Escape of an IME composition and one a control already handled', () => {
    const onEscape = vi.fn()
    layer({ onEscape })
    press('Escape', { isComposing: true })
    press('Escape', { keyCode: 229 })
    const handled = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
    handled.preventDefault()
    document.dispatchEvent(handled)
    expect(onEscape).not.toHaveBeenCalled()
  })

  // A naive-ui modal (or any dialog that does not use the stack) opened above a
  // layer: its keys are its own, not the layer's.
  it('leaves keys from a modal that did not join the stack to that modal', () => {
    const root = document.createElement('div')
    const foreign = document.createElement('div')
    foreign.setAttribute('aria-modal', 'true')
    const field = document.createElement('input')
    foreign.appendChild(field)
    document.body.append(root, foreign)
    const onEscape = vi.fn()
    const onKeydown = vi.fn()
    layer({ root: () => root, onEscape, onKeydown })

    press('Escape', {}, field)
    press('Tab', {}, field)
    expect(onEscape).not.toHaveBeenCalled()
    expect(onKeydown).not.toHaveBeenCalled()

    press('Escape', {}, document.body)
    expect(onEscape).toHaveBeenCalledTimes(1)
  })

  it('stops listening when the stack is empty, and starts again', async () => {
    const remove = vi.spyOn(document, 'removeEventListener')
    const only = layer({ onEscape: vi.fn() })
    only.open.value = false
    await nextTick()
    expect(remove).toHaveBeenCalledWith('keydown', expect.any(Function))
    remove.mockRestore()

    const onEscape = vi.fn()
    layer({ onEscape })
    press('Escape')
    expect(onEscape).toHaveBeenCalledTimes(1)
  })

  // Two copies of this package on a page share core's stack, and both copies'
  // document listeners see every key press: the top layer must still get it once.
  it('two copies of the module hand one key press to the top layer once', async () => {
    vi.resetModules()
    const other = await import('../../../src/headless/feedback/useOverlayLayer')
    const below = vi.fn()
    const above = vi.fn()
    layer({ onEscape: below })
    const scope = effectScope()
    scopes.push(scope)
    scope.run(() => other.useOverlayLayer(() => true, { onEscape: above }))

    press('Escape')
    expect(above).toHaveBeenCalledTimes(1)
    expect(below).not.toHaveBeenCalled()
  })
})

describe('useFocusTrap in the stack', () => {
  function trap(onEscape = vi.fn()) {
    const root = document.createElement('div')
    root.innerHTML = '<button data-first>first</button><button data-last>last</button>'
    document.body.appendChild(root)
    const open = ref(true)
    const scope = effectScope()
    scopes.push(scope)
    scope.run(() => useFocusTrap(ref(root), () => open.value, { onEscape }))
    return {
      root,
      open,
      onEscape,
      first: root.querySelector<HTMLButtonElement>('[data-first]')!,
      last: root.querySelector<HTMLButtonElement>('[data-last]')!,
    }
  }

  it('keeps Tab inside the dialog opened on top, not the one under it', () => {
    const settings = trap()
    const confirm = trap()
    confirm.last.focus()

    press('Tab', {}, confirm.last)
    expect(document.activeElement).toBe(confirm.first)

    confirm.first.focus()
    press('Tab', {}, confirm.first)
    // Not dragged back into the dialog underneath.
    expect(settings.root.contains(document.activeElement)).toBe(false)
  })

  it('closes only the dialog on top on Escape, then the one under it', async () => {
    const settings = trap()
    const confirm = trap()
    confirm.onEscape.mockImplementation(() => (confirm.open.value = false))

    press('Escape')
    await nextTick()
    expect(confirm.onEscape).toHaveBeenCalledTimes(1)
    expect(settings.onEscape).not.toHaveBeenCalled()

    press('Escape')
    expect(settings.onEscape).toHaveBeenCalledTimes(1)
  })
})
