import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { h } from 'vue'
import TItemCard from '../../../src/components/data/TItemCard.vue'

/** Dispatch a real KeyboardEvent so `defaultPrevented` can be inspected. */
function press(el: Element, key: string, type: 'keydown' | 'keyup' = 'keydown'): KeyboardEvent {
  const event = new KeyboardEvent(type, { key, bubbles: true, cancelable: true })
  el.dispatchEvent(event)
  return event
}

function mountCard(props: Record<string, unknown> = {}, slots: Record<string, unknown> = {}) {
  return mount(TItemCard, {
    props: { title: 'INV-000001', ...props },
    slots,
  })
}

describe('TItemCard', () => {
  describe('clickable mode', () => {
    it.each([['Enter'], [' ']])('activates on %s and prevents the default', (key) => {
      const w = mountCard({ clickable: true })

      const event = press(w.find('.t-item-card').element, key)

      expect(w.emitted('click')).toHaveLength(1)
      // Space would otherwise scroll the page out from under the card.
      expect(event.defaultPrevented).toBe(true)
    })

    it('ignores keys that do not activate a button', () => {
      const w = mountCard({ clickable: true })

      const tab = press(w.find('.t-item-card').element, 'Tab')

      expect(w.emitted('click')).toBeUndefined()
      expect(tab.defaultPrevented).toBe(false)
    })

    it.each([['Enter'], [' ']])('leaves a non-clickable card inert on %s', async (key) => {
      const w = mountCard()

      const event = press(w.find('.t-item-card').element, key)
      await w.find('.t-item-card').trigger('click')

      expect(w.emitted('click')).toBeUndefined()
      expect(event.defaultPrevented).toBe(false)
    })
  })

  describe('actions strip shields the card', () => {
    // The strip stops both activation keys but prevents neither: a native button
    // activates on Space at keyup, so preventing here would break the control we
    // are trying to protect.
    it.each([['Enter'], [' ']])('does not open the card when %s hits a row action', (key) => {
      const onActionKeydown = vi.fn()
      const w = mountCard(
        { clickable: true },
        { actions: () => h('button', { class: 'act', onKeydown: onActionKeydown }, 'Edit') },
      )

      const event = press(w.find('button.act').element, key)

      expect(w.emitted('click')).toBeUndefined()
      // The control itself still hears the key, and keeps its default behaviour.
      expect(onActionKeydown).toHaveBeenCalledTimes(1)
      expect(event.defaultPrevented).toBe(false)
    })

    it('still lets a row action click through to its own handler only', async () => {
      const onActionClick = vi.fn()
      const w = mountCard(
        { clickable: true },
        { actions: () => h('button', { class: 'act', onClick: onActionClick }, 'Edit') },
      )

      await w.find('button.act').trigger('click')

      expect(onActionClick).toHaveBeenCalledTimes(1)
      expect(w.emitted('click')).toBeUndefined()
    })
  })

  describe('footer band', () => {
    it('renders under the row, and only when given', () => {
      expect(mountCard().find('.t-item-card__footer').exists()).toBe(false)

      const w = mountCard({}, { footer: () => h('ul', { class: 'keys' }, 'two keys') })

      expect(w.classes()).toContain('t-item-card--with-footer')
      expect(w.find('.t-item-card__footer ul.keys').text()).toBe('two keys')
    })

    it('keeps its own controls from opening the card, like the operations', async () => {
      const onRemove = vi.fn()
      const w = mountCard(
        { clickable: true },
        { footer: () => h('button', { class: 'rm', onClick: onRemove }, 'Remove') },
      )

      await w.find('button.rm').trigger('click')
      press(w.find('button.rm').element, 'Enter')

      expect(onRemove).toHaveBeenCalledTimes(1)
      expect(w.emitted('click')).toBeUndefined()
    })
  })

  describe('selection checkbox shields the card', () => {
    it('does not open the card when Space toggles the checkbox, and the toggle still fires', () => {
      const w = mountCard({ clickable: true, selectable: true })
      const box = w.find('.n-checkbox')
      expect(box.exists()).toBe(true)

      // NCheckbox toggles on keyup; the card listens on keydown. Cutting the
      // keydown at the shield is what keeps these two apart.
      press(box.element, ' ')
      press(box.element, ' ', 'keyup')

      expect(w.emitted('click')).toBeUndefined()
      expect(w.emitted('update:checked')).toEqual([[true]])
    })

    it('does not open the card when Enter toggles the checkbox', () => {
      const w = mountCard({ clickable: true, selectable: true })
      const box = w.find('.n-checkbox')

      const event = press(box.element, 'Enter')
      press(box.element, 'Enter', 'keyup')

      expect(w.emitted('click')).toBeUndefined()
      expect(w.emitted('update:checked')).toEqual([[true]])
      // Stopped, not prevented - naive only prevents Space (to kill the scroll).
      expect(event.defaultPrevented).toBe(false)
    })

    it('does not open the card when the checkbox is clicked', async () => {
      const w = mountCard({ clickable: true, selectable: true })

      await w.find('.n-checkbox').trigger('click')

      expect(w.emitted('click')).toBeUndefined()
      expect(w.emitted('update:checked')).toEqual([[true]])
    })
  })
})
