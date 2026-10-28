import { describe, it, expect, vi, afterEach } from 'vitest'
import { effectScope, ref } from 'vue'
import { useReorderable } from '../../src/headless/data/useReorderable'

/**
 * Items that open on click must not open when a drag releases on them: the pointer fallback (touch, and browsers
 * without native drag-and-drop) can deliver a click on release. The reorder primitive swallows that one click
 * through `dragBindings.onClickCapture`, so every surface binding it gets the guard.
 */
function setup() {
  const scope = effectScope()
  const r = scope.run(() =>
    useReorderable<number>({
      source: () => [1, 2, 3],
      enabled: () => true,
      onCommit: () => undefined,
      ghostClass: 'g',
      chosenClass: 'c',
    }),
  )!
  const b = r.dragBindings.value as Record<string, (arg?: unknown) => void>
  return { b, scope }
}

function click() {
  return { stopPropagation: vi.fn(), preventDefault: vi.fn() }
}

describe('useReorderable release click', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('lets a plain click through', () => {
    const { b, scope } = setup()
    const e = click()
    b.onClickCapture(e)
    expect(e.stopPropagation).not.toHaveBeenCalled()
    scope.stop()
  })

  it('swallows the first click right after a gesture ends, and only that one', () => {
    vi.spyOn(Date, 'now').mockReturnValue(10_000)
    const { b, scope } = setup()
    b.onStart()
    b.onEnd({ oldIndex: 0, newIndex: 2 })

    const released = click()
    b.onClickCapture(released)
    expect(released.stopPropagation).toHaveBeenCalled()
    expect(released.preventDefault).toHaveBeenCalled()

    const next = click()
    b.onClickCapture(next)
    expect(next.stopPropagation).not.toHaveBeenCalled()
    scope.stop()
  })

  it('a click long after the gesture is an ordinary click', () => {
    const now = vi.spyOn(Date, 'now').mockReturnValue(10_000)
    const { b, scope } = setup()
    b.onStart()
    b.onEnd({ oldIndex: 1, newIndex: 1 })
    now.mockReturnValue(10_500)

    const e = click()
    b.onClickCapture(e)
    expect(e.stopPropagation).not.toHaveBeenCalled()
    scope.stop()
  })

  it('binds nothing while reordering is off', () => {
    const scope = effectScope()
    const on = ref(false)
    const r = scope.run(() =>
      useReorderable<number>({ source: () => [1], enabled: () => on.value, onCommit: () => undefined, ghostClass: 'g', chosenClass: 'c' }),
    )!
    expect(r.dragBindings.value.onClickCapture).toBeUndefined()
    scope.stop()
  })
})
