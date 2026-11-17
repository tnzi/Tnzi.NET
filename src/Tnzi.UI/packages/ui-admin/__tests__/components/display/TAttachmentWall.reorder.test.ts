import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'

vi.mock('naive-ui', () => ({
  NImage: { name: 'NImage', props: ['src', 'width', 'height', 'objectFit'], template: '<img class="nimg" :src="src" />' },
  NImageGroup: { name: 'NImageGroup', template: '<div class="nimggroup"><slot /></div>' },
}))
vi.mock('@iconify/vue', () => ({
  Icon: { name: 'Icon', props: ['icon'], template: '<i class="icon" :data-icon="icon" />' },
}))

/**
 * Handlers and options the stubbed VueDraggable captured on its last render, so
 * a test can drive a gesture without a real pointer.
 */
const drag: {
  attrs?: Record<string, unknown>
  onStart?: () => void
  onEnd?: (evt: { oldIndex?: number | null; newIndex?: number | null }) => void
  setModel?: (next: unknown[]) => void
} = {}

// The optional peer. Declaring `modelValue` as a prop keeps it out of `$attrs`,
// exactly as the real one behaves; everything else stays in attrs so a test can
// read the Sortable options the component actually asked for.
vi.mock('vue-draggable-plus', () => ({
  VueDraggable: defineComponent({
    name: 'VueDraggableStub',
    props: { modelValue: { type: Array, default: () => [] } },
    setup(_props, { attrs, slots }) {
      return () => {
        drag.attrs = attrs as Record<string, unknown>
        drag.onStart = attrs.onStart as typeof drag.onStart
        drag.onEnd = attrs.onEnd as typeof drag.onEnd
        drag.setModel = attrs['onUpdate:modelValue'] as typeof drag.setModel
        return h('div', { class: attrs.class as string, 'data-drag': 'on' }, slots.default?.())
      }
    },
  }),
}))

import TAttachmentWall, { type Attachment } from '../../../src/components/display/TAttachmentWall.vue'

const FILES = [
  { url: '/a.png', name: 'a.png' },
  { url: '/b.png', name: 'b.png' },
  { url: '/c.png', name: 'c.png' },
]

let wrapper: VueWrapper | undefined

function mountWall(props: Record<string, unknown> = {}) {
  wrapper = mount(TAttachmentWall, {
    props: { attachments: FILES, ...props },
    attachTo: document.body,
  })
  return wrapper
}

/** Dispatch a real KeyboardEvent so `defaultPrevented` can be inspected. */
function press(el: Element, key: string, altKey = true): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, altKey, bubbles: true, cancelable: true })
  el.dispatchEvent(event)
  return event
}

const names = (w: VueWrapper): string[] =>
  w.findAll('.t-attachment-wall__tile').map((t) => t.find('.t-attachment-wall__img').attributes('src') ?? '')

type ReorderPayload = {
  attachments: Attachment[]
  orderedKeys: (string | number)[]
  from: number
  to: number
  moved: Attachment
  revert: () => void
}

const lastReorder = (w: VueWrapper): ReorderPayload =>
  (w.emitted('reorder')?.at(-1)?.[0] ?? undefined) as ReorderPayload

beforeEach(() => {
  wrapper = undefined
  drag.attrs = undefined
  drag.onStart = undefined
  drag.onEnd = undefined
  drag.setModel = undefined
})
afterEach(() => {
  wrapper?.unmount()
})

describe('TAttachmentWall - reordering off (the default)', () => {
  it('renders exactly what it rendered before: no wrapper, no tab stop, no grip, no live region', () => {
    const w = mountWall()

    // The tiles must stay DIRECT children of the image group, because
    // `.t-attachment-wall` is the flex container they lay out in. An element in
    // between would re-flow every wall that exists today.
    const group = w.find('.nimggroup').element
    expect(group.children).toHaveLength(3)
    expect(group.children[0]?.classList.contains('t-attachment-wall__tile')).toBe(true)

    expect(w.find('.t-attachment-wall__strip').exists()).toBe(false)
    expect(w.find('[data-drag="on"]').exists()).toBe(false)
    expect(w.find('.t-attachment-wall__sr').exists()).toBe(false)
    expect(w.find('.t-attachment-wall__grip').exists()).toBe(false)

    const tile = w.find('.t-attachment-wall__tile')
    expect(tile.attributes('tabindex')).toBeUndefined()
    expect(tile.attributes('aria-label')).toBeUndefined()
    // No reorder modifier. Asserted by absence rather than by an exact class
    // list: the tile also carries `t-media-frame`, which is the remove chip's
    // hover host and is there whether or not the wall reorders.
    expect(tile.classes()).not.toContain('t-attachment-wall__tile--reorderable')
  })

  it('ignores Alt + arrow keys entirely', async () => {
    const w = mountWall()

    press(w.findAll('.t-attachment-wall__tile')[0]!.element, 'ArrowRight')
    await nextTick()

    expect(w.emitted('reorder')).toBeUndefined()
    expect(names(w)).toEqual(['/a.png', '/b.png', '/c.png'])
  })
})

describe('TAttachmentWall - reordering on', () => {
  it('makes every tile focusable and says where it sits', () => {
    const w = mountWall({ reorderable: true })

    const tiles = w.findAll('.t-attachment-wall__tile')
    expect(tiles.map((t) => t.attributes('tabindex'))).toEqual(['0', '0', '0'])
    expect(tiles[1]!.attributes('aria-label')).toBe(
      'b.png, 2 / 3. Use Alt with the arrow keys to reorder.',
    )
    expect(w.find('.t-attachment-wall__sr').attributes('aria-live')).toBe('polite')
  })

  it('moves a tile with Alt + arrow and reports the new order', async () => {
    const w = mountWall({ reorderable: true })

    press(w.findAll('.t-attachment-wall__tile')[0]!.element, 'ArrowRight')
    await nextTick()

    expect(names(w)).toEqual(['/b.png', '/a.png', '/c.png'])

    const payload = lastReorder(w)
    expect(payload.orderedKeys).toEqual(['/b.png', '/a.png', '/c.png'])
    expect(payload.attachments.map((a) => a.url)).toEqual(['/b.png', '/a.png', '/c.png'])
    expect(payload.from).toBe(0)
    expect(payload.to).toBe(1)
    expect(payload.moved.url).toBe('/a.png')
  })

  it('★ leaves the tiles alone without Alt - the bare arrows still mean "look at the next tile"', async () => {
    // A wall is a grid: binding the bare arrows to a move makes people rearrange
    // content when they only meant to browse it.
    const w = mountWall({ reorderable: true })

    const event = press(w.findAll('.t-attachment-wall__tile')[0]!.element, 'ArrowRight', false)
    await nextTick()

    expect(w.emitted('reorder')).toBeUndefined()
    expect(names(w)).toEqual(['/a.png', '/b.png', '/c.png'])
    expect(event.defaultPrevented).toBe(false)
  })

  it('★ swallows Alt + Left on the first tile instead of navigating the browser back', async () => {
    // Alt + Left is Back on Windows. Preventing it only when the move succeeds
    // would send the operator off the page from the very first tile.
    const w = mountWall({ reorderable: true })

    const event = press(w.findAll('.t-attachment-wall__tile')[0]!.element, 'ArrowLeft')
    await nextTick()

    expect(event.defaultPrevented).toBe(true)
    expect(w.emitted('reorder')).toBeUndefined()
    expect(names(w)).toEqual(['/a.png', '/b.png', '/c.png'])
  })

  it('supports Alt + End and Alt + Home', async () => {
    const w = mountWall({ reorderable: true })

    press(w.findAll('.t-attachment-wall__tile')[0]!.element, 'End')
    await nextTick()
    expect(names(w)).toEqual(['/b.png', '/c.png', '/a.png'])

    press(w.findAll('.t-attachment-wall__tile')[2]!.element, 'Home')
    await nextTick()
    expect(names(w)).toEqual(['/a.png', '/b.png', '/c.png'])
  })

  it('★ keeps focus on the tile that moved even when the wall is keyed by index', async () => {
    // Deliberately index-keyed. With a stable key Vue MOVES the node and focus
    // rides along for free, so this assertion would pass with no focus handling
    // at all; only the index-keyed wall (which Vue patches in place) can tell
    // the difference. Losing focus here would mean one Alt+Right per keypress
    // and then hunting for the tile again.
    const w = mountWall({ reorderable: true, itemKey: (_a: unknown, i: number) => i })

    const first = w.findAll('.t-attachment-wall__tile')[0]!.element as HTMLElement
    first.focus()
    press(first, 'ArrowRight')
    await nextTick()

    const tiles = w.findAll('.t-attachment-wall__tile')
    expect(names(w)).toEqual(['/b.png', '/a.png', '/c.png'])
    expect(document.activeElement).toBe(tiles[1]!.element)
  })

  it('announces the move for anyone who cannot see it happen', async () => {
    const w = mountWall({ reorderable: true })

    expect(w.find('.t-attachment-wall__sr').text()).toBe('')
    press(w.findAll('.t-attachment-wall__tile')[0]!.element, 'ArrowRight')
    await nextTick()

    expect(w.find('.t-attachment-wall__sr').text()).toBe('a.png, 2 / 3')
  })

  it('★ revert() puts the wall back when the caller could not persist the order', async () => {
    // The move is applied optimistically, so without this the screen keeps
    // showing an order the server rejected and nothing says the change was lost.
    const w = mountWall({ reorderable: true })

    press(w.findAll('.t-attachment-wall__tile')[0]!.element, 'ArrowRight')
    await nextTick()
    expect(names(w)).toEqual(['/b.png', '/a.png', '/c.png'])

    lastReorder(w).revert()
    await nextTick()

    expect(names(w)).toEqual(['/a.png', '/b.png', '/c.png'])
  })

  it('never mutates the attachments prop', async () => {
    const source = FILES.map((f) => ({ ...f }))
    wrapper = mount(TAttachmentWall, {
      props: { attachments: source, reorderable: true },
      attachTo: document.body,
    })

    press(wrapper.findAll('.t-attachment-wall__tile')[0]!.element, 'ArrowRight')
    await nextTick()

    expect(source.map((f) => f.url)).toEqual(['/a.png', '/b.png', '/c.png'])
  })

  it('shows its tiles on the very first tick, before the drag module can have arrived', () => {
    // `vue-draggable-plus` is an OPTIONAL peer loaded on demand, so the strip
    // has to render its children while the chunk is still in flight. (That it
    // also survives the module never arriving is proved in
    // `TAttachmentWall.reorder-nodrag.test.ts`, where the import rejects.)
    const w = mountWall({ reorderable: true })

    expect(w.findAll('.t-attachment-wall__tile')).toHaveLength(3)
  })

  it('keeps add and remove working, and keeps them out of the drag container', async () => {
    const w = mountWall({ reorderable: true, removable: true, addable: true })

    await w.find('.t-attachment-wall__remove').trigger('click')
    expect(w.emitted('remove')?.[0]?.[0]).toEqual(FILES[0])

    await w.find('.t-attachment-wall__add').trigger('click')
    expect(w.emitted('add')).toBeTruthy()

    // The add tile is a sibling of the strip, so a tile can never be dropped
    // after it.
    await flushPromises()
    expect(w.find('[data-drag="on"] .t-attachment-wall__add').exists()).toBe(false)
  })
})

describe('TAttachmentWall - what starts a gesture', () => {
  it('★ picks the drag surface positively (a handle), never by filtering tags out', async () => {
    // The regression this exists for: `filter: 'button, a, ...'` reads as
    // "everything else is draggable", but Sortable matches the filter against
    // the press target AND its ancestors - and a non-image tile is one `<a>`
    // stretched over the whole tile. Every pixel of a PDF tile matched, so the
    // gesture was refused and a wall of documents ignored the mouse entirely.
    const w = mountWall({ reorderable: true })
    await flushPromises()

    expect(drag.attrs?.handle).toBe('.t-attachment-wall__grip')
    expect(drag.attrs?.filter).toBeUndefined()
    expect(w.findAll('.t-attachment-wall__grip')).toHaveLength(3)
  })

  it('puts a grip on a NON-image tile too - the case the filter used to break', async () => {
    wrapper = mount(TAttachmentWall, {
      props: {
        attachments: [{ url: '/report.pdf', name: 'report.pdf' }],
        reorderable: true,
      },
      attachTo: document.body,
    })
    await flushPromises()

    // The tile really is the anchor-filling shape described above...
    expect(wrapper.find('.t-attachment-wall__file').exists()).toBe(true)
    // ...and it still has a grip that no filter can swallow.
    expect(wrapper.find('.t-attachment-wall__tile .t-attachment-wall__grip').exists()).toBe(true)
  })

  it('keeps the grip out of the accessibility tree - the keyboard path is the tile itself', async () => {
    const w = mountWall({ reorderable: true })
    await flushPromises()

    const grip = w.find('.t-attachment-wall__grip')
    expect(grip.attributes('aria-hidden')).toBe('true')
    expect(grip.attributes('tabindex')).toBeUndefined()
  })
})

describe('TAttachmentWall - dragging', () => {
  async function mountDragging(props: Record<string, unknown> = {}) {
    const w = mountWall({ reorderable: true, ...props })
    await flushPromises()
    expect(drag.onEnd, 'the stubbed draggable captured its handlers').toBeTypeOf('function')
    return w
  }

  /** What VueDraggable really does: write the model back, THEN fire `end`. */
  function dropTile(from: number, to: number, order: Attachment[]): void {
    drag.onStart?.()
    drag.setModel?.(order)
    drag.onEnd?.({ oldIndex: from, newIndex: to })
  }

  it('reports a drop with the same payload a keyboard move produces', async () => {
    const w = await mountDragging()

    dropTile(2, 0, [FILES[2]!, FILES[0]!, FILES[1]!])
    await nextTick()

    const payload = lastReorder(w)
    expect(payload.from).toBe(2)
    expect(payload.to).toBe(0)
    expect(payload.moved.url).toBe('/c.png')
    expect(payload.orderedKeys).toEqual(['/c.png', '/a.png', '/b.png'])
    expect(names(w)).toEqual(['/c.png', '/a.png', '/b.png'])
  })

  it('★ revert() undoes a drop, not just a keypress', async () => {
    const w = await mountDragging()

    dropTile(0, 2, [FILES[1]!, FILES[2]!, FILES[0]!])
    await nextTick()
    expect(names(w)).toEqual(['/b.png', '/c.png', '/a.png'])

    lastReorder(w).revert()
    await nextTick()
    expect(names(w)).toEqual(['/a.png', '/b.png', '/c.png'])
  })

  it('says nothing when the tile is dropped back where it came from', async () => {
    const w = await mountDragging()

    drag.onStart?.()
    drag.onEnd?.({ oldIndex: 1, newIndex: 1 })
    await nextTick()

    expect(w.emitted('reorder')).toBeUndefined()
  })

  it('★ ignores a prop change that lands mid-gesture', async () => {
    // Reassigning the working copy while a pointer is down severs Sortable's
    // tracking, and what the operator sees is the tile dropping somewhere they
    // never let go of. A list that refreshes on a timer is enough to trigger it.
    const w = await mountDragging()

    drag.onStart?.()
    await w.setProps({ attachments: [FILES[2]!, FILES[1]!, FILES[0]!] })
    await nextTick()

    expect(names(w)).toEqual(['/a.png', '/b.png', '/c.png'])

    // ...and once the gesture is over the wall follows the prop again.
    drag.onEnd?.({ oldIndex: 0, newIndex: 0 })
    await w.setProps({ attachments: [FILES[1]!, FILES[0]!, FILES[2]!] })
    await nextTick()
    expect(names(w)).toEqual(['/b.png', '/a.png', '/c.png'])
  })

  it('holds a press on touch long enough to tell a drag from a scroll', async () => {
    await mountDragging()

    // A finger swiping to scroll the form must not pick a tile up; the mouse
    // keeps zero delay, which is what `delayOnTouchOnly` says.
    expect(drag.attrs?.delay).toBe(180)
    expect(drag.attrs?.delayOnTouchOnly).toBe(true)
  })
})

describe('TAttachmentWall - reordering and the #tile slot', () => {
  it('★ leaves Alt + arrows alone when the keypress came from inside a tile', async () => {
    // The tile is what announces the shortcut, so it is what the shortcut acts
    // on. A slot can hold its own focusable content (the documented example is
    // a file link) and must keep browser Back.
    wrapper = mount(TAttachmentWall, {
      props: { attachments: FILES, reorderable: true },
      slots: { tile: '<a class="host-link" href="/x">open</a>' },
      attachTo: document.body,
    })

    const link = wrapper.findAll('.host-link')[0]!.element
    const event = press(link, 'ArrowRight')
    await nextTick()

    expect(event.defaultPrevented).toBe(false)
    expect(wrapper.emitted('reorder')).toBeUndefined()
  })
})
