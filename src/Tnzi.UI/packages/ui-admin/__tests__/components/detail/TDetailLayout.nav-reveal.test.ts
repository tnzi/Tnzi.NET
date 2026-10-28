import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import TDetailLayout from '../../../src/components/detail/TDetailLayout.vue'

/**
 * The `side` nav keeps the selected item in view inside its own scroll box: on a deep link, on any change of the
 * active section and when the list changes under it. jsdom has no layout, so item positions are faked: the nav box
 * is 100px tall and each item's top is its slot below minus the nav's `scrollTop`. The real check is visual.
 */
const NAV_HEIGHT = 100
const ITEM_HEIGHT = 40
// basic and perms are visible at scrollTop 0; billing sits far below the fold
const SLOT_TOP: Record<string, number> = { basic: 0, perms: 40, billing: 400 }

const MenuStub = defineComponent({
  name: 'Menu',
  props: { value: { type: String, default: undefined }, options: { type: Array, default: () => [] } },
  setup(props) {
    return () => {
      const flat = (props.options as Array<{ key: string; children?: Array<{ key: string }> }>).flatMap((o) => o.children ?? [o])
      return h(
        'div',
        { class: 'nav-stub' },
        flat.map((o) =>
          h('div', {
            key: o.key,
            'data-key': o.key,
            class: ['n-menu-item-content', o.key === props.value && 'n-menu-item-content--selected'],
          }),
        ),
      )
    }
  },
})

const stubs = { Menu: MenuStub, SvgIcon: true, Popover: true }
const sections = [
  { key: 'basic', label: 'Basic' },
  { key: 'perms', label: 'Perms' },
  { key: 'billing', label: 'Billing', group: 'billing' },
]

function rect(top: number, height: number): DOMRect {
  return { top, bottom: top + height, left: 0, right: 0, width: 0, height, x: 0, y: top, toJSON: () => ({}) } as DOMRect
}

let scrollIntoView: ReturnType<typeof vi.fn>

beforeEach(() => {
  scrollIntoView = vi.fn()
  Element.prototype.scrollIntoView = scrollIntoView as unknown as Element['scrollIntoView']
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const key = this.getAttribute('data-key')
    if (key == null) return rect(0, NAV_HEIGHT)
    const nav = this.closest('.nav-stub') as HTMLElement
    return rect(SLOT_TOP[key] - nav.scrollTop, ITEM_HEIGHT)
  })
})

afterEach(() => {
  vi.restoreAllMocks()
})

function mountAt(activeSection: string) {
  return mount(TDetailLayout, {
    props: { layout: 'side', title: 'Settings', sections, activeSection },
    slots: { default: '<div class="body" />' },
    global: { stubs },
    attachTo: document.body,
  })
}

function navOf(w: ReturnType<typeof mountAt>): HTMLElement {
  return w.find('.nav-stub').element as HTMLElement
}

describe('TDetailLayout side nav keeps the selected item in view', () => {
  it('a deep link to an item below the fold scrolls the nav to it, by the shortest distance', async () => {
    const w = mountAt('billing')
    await nextTick()
    // item bottom 440 against a 100px box: scrolled just far enough to show it
    expect(navOf(w).scrollTop).toBe(SLOT_TOP.billing + ITEM_HEIGHT - NAV_HEIGHT)
    w.unmount()
  })

  it('a change of the active section scrolls to it', async () => {
    const w = mountAt('basic')
    await nextTick()
    expect(navOf(w).scrollTop).toBe(0)

    await w.setProps({ activeSection: 'billing' })
    await nextTick()
    expect(navOf(w).scrollTop).toBe(340)
    w.unmount()
  })

  it('an item above the visible area is brought down to the top edge', async () => {
    const w = mountAt('billing')
    await nextTick()
    await w.setProps({ activeSection: 'basic' })
    await nextTick()
    expect(navOf(w).scrollTop).toBe(0)
    w.unmount()
  })

  it('selecting an item that is already visible leaves the list where it is', async () => {
    const w = mountAt('basic')
    await nextTick()
    await w.setProps({ activeSection: 'perms' })
    await nextTick()
    expect(navOf(w).scrollTop).toBe(0)
    w.unmount()
  })

  it('a list that changes under the selection (a cleared search) re-reveals it', async () => {
    const w = mountAt('billing')
    await nextTick()
    navOf(w).scrollTop = 0
    await w.setProps({ sections: [...sections] })
    await nextTick()
    expect(navOf(w).scrollTop).toBe(340)
    w.unmount()
  })

  it('never uses scrollIntoView, which would scroll the page as well', async () => {
    const w = mountAt('billing')
    await nextTick()
    await w.setProps({ activeSection: 'basic' })
    await nextTick()
    expect(scrollIntoView).not.toHaveBeenCalled()
    w.unmount()
  })
})
