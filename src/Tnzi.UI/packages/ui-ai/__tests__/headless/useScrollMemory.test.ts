import { describe, it, expect } from 'vitest'
import { nextTick } from 'vue'
import { useScrollMemory } from '../../src/headless/useScrollMemory'

/** A scroller whose scrollTop is clamped to its content, as in a browser. */
function scroller(height: number, clientHeight = 500) {
  const el = document.createElement('div')
  document.body.appendChild(el)
  const geometry = { height, top: 0 }
  Object.defineProperty(el, 'scrollHeight', { configurable: true, get: () => geometry.height })
  Object.defineProperty(el, 'clientHeight', { configurable: true, get: () => clientHeight })
  Object.defineProperty(el, 'scrollTop', {
    configurable: true,
    get: () => geometry.top,
    set: (v: number) => { geometry.top = Math.max(0, Math.min(v, geometry.height - clientHeight)) },
  })
  return { el, geometry }
}

async function grow(el: HTMLElement, geometry: { height: number }, height: number) {
  geometry.height = height
  el.appendChild(document.createElement('p'))
  await Promise.resolve()
  await nextTick()
}

describe('useScrollMemory', () => {
  it('puts each key back where it was saved, and a new key at the top', () => {
    const memory = useScrollMemory()
    const { el } = scroller(3000)
    el.scrollTop = 400
    memory.save('agents', el)
    el.scrollTop = 900
    memory.save('skills', el)
    memory.restore('agents', el)
    expect(el.scrollTop).toBe(400)
    memory.restore('skills', el)
    expect(el.scrollTop).toBe(900)
    memory.restore('knowledge', el)
    expect(el.scrollTop).toBe(0)
  })

  it('holds the position until content that arrives later is tall enough', async () => {
    const memory = useScrollMemory()
    const { el, geometry } = scroller(3000)
    el.scrollTop = 1200
    memory.save('page', el)
    geometry.height = 600 // the page came back empty, its data still loading
    memory.restore('page', el)
    expect(el.scrollTop).toBe(100)
    await grow(el, geometry, 1000)
    expect(el.scrollTop).toBe(500)
    await grow(el, geometry, 3000)
    expect(el.scrollTop).toBe(1200)
  })

  it('stops as soon as the user scrolls', async () => {
    const memory = useScrollMemory()
    const { el, geometry } = scroller(3000)
    el.scrollTop = 1200
    memory.save('page', el)
    geometry.height = 600
    memory.restore('page', el)
    el.dispatchEvent(new Event('touchstart'))
    await grow(el, geometry, 3000)
    expect(el.scrollTop).toBe(100)
  })

  it('a later restore replaces one in progress', async () => {
    const memory = useScrollMemory()
    const a = scroller(3000)
    a.el.scrollTop = 1200
    memory.save('a', a.el)
    a.geometry.height = 600
    memory.restore('a', a.el)
    memory.restore('b', a.el)
    await grow(a.el, a.geometry, 3000)
    expect(a.el.scrollTop).toBe(0)
  })

  it('forgets a key', () => {
    const memory = useScrollMemory()
    const { el } = scroller(3000)
    el.scrollTop = 300
    memory.save('a', el)
    expect(memory.saved('a')).toBe(300)
    memory.forget('a')
    expect(memory.saved('a')).toBeUndefined()
  })
})
