/**
 * What happens when the optional peer is simply not there.
 *
 * `vue-draggable-plus` is declared `optional: true` in `peerDependenciesMeta`,
 * so a consumer who installs `@tnzi/ui` and turns `reorderable` on without it
 * is doing nothing wrong. This file makes the dynamic import REJECT and asserts
 * the wall survives it.
 *
 * ★ The failure this guards against is not "dragging stops working" - it is the
 * list going blank. `defineAsyncComponent` with no `errorComponent` renders
 * `null` on a failed load, and since the draggable IS the container of every
 * tile, the whole wall would disappear. Two other components in this monorepo
 * shipped exactly that bug before the fallback moved into `useReorderable`.
 *
 * It lives in its own file because `vi.mock` is file-scoped: a rejecting module
 * and a resolving one cannot coexist in one suite.
 */
import { describe, it, expect, vi, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'

vi.mock('naive-ui', () => ({
  NImage: { name: 'NImage', props: ['src', 'width', 'height', 'objectFit'], template: '<img class="nimg" :src="src" />' },
  NImageGroup: { name: 'NImageGroup', template: '<div class="nimggroup"><slot /></div>' },
}))
vi.mock('@iconify/vue', () => ({
  Icon: { name: 'Icon', props: ['icon'], template: '<i class="icon" :data-icon="icon" />' },
}))

// The peer is not installed.
vi.mock('vue-draggable-plus', () => {
  throw new Error('Cannot find module \'vue-draggable-plus\'')
})

import TAttachmentWall from '../../../src/components/display/TAttachmentWall.vue'

const FILES = [
  { url: '/a.png', name: 'a.png' },
  { url: '/b.png', name: 'b.png' },
  { url: '/c.png', name: 'c.png' },
]

let wrapper: VueWrapper | undefined
afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

const names = (w: VueWrapper): string[] =>
  w.findAll('.t-attachment-wall__tile').map((t) => t.find('.t-attachment-wall__img').attributes('src') ?? '')

describe('TAttachmentWall - the drag module never arrives', () => {
  it('★ still renders every tile', async () => {
    // Vue logs the load failure; the assertion is that the wall is not empty.
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.spyOn(console, 'error').mockImplementation(() => {})

    wrapper = mount(TAttachmentWall, {
      props: { attachments: FILES, reorderable: true },
      attachTo: document.body,
    })
    await flushPromises()

    expect(wrapper.findAll('.t-attachment-wall__tile')).toHaveLength(3)
    expect(names(wrapper)).toEqual(['/a.png', '/b.png', '/c.png'])
  })

  it('★ still reorders from the keyboard - that path never needed the module', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.spyOn(console, 'error').mockImplementation(() => {})

    wrapper = mount(TAttachmentWall, {
      props: { attachments: FILES, reorderable: true },
      attachTo: document.body,
    })
    await flushPromises()

    const tile = wrapper.findAll('.t-attachment-wall__tile')[0]!.element
    tile.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', altKey: true, bubbles: true, cancelable: true }))
    await nextTick()

    expect(names(wrapper)).toEqual(['/b.png', '/a.png', '/c.png'])
    expect(wrapper.emitted('reorder')).toHaveLength(1)
  })

  it('keeps the strip layout, so the wall does not re-flow into a column', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.spyOn(console, 'error').mockImplementation(() => {})

    wrapper = mount(TAttachmentWall, {
      props: { attachments: FILES, reorderable: true },
      attachTo: document.body,
    })
    await flushPromises()

    // The stand-in passes `class` through by hand precisely for this.
    expect(wrapper.find('.t-attachment-wall__strip').exists()).toBe(true)
  })
})
